using System.Text.Json;
using System.Threading.Channels;
using Npgsql;

namespace ChatService.Services;

// One row this app wants to write to gemini_usage_log. Mirrors the
// table's columns (minus id, DB-generated) - see docs/log-dashboard.md
// section 1.1. Duplicated from search-service's identical class - see
// GeminiPricing.cs's header comment for why.
public class UsageRecord
{
    public required string ServiceName { get; init; }
    public string? Operation { get; init; }
    public required string Model { get; init; }
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
    public int? TotalTokens { get; init; }
    public bool IsEstimated { get; init; }
    public decimal? CostUsd { get; init; }
    public string? SessionId { get; init; }
    public object? RawUsage { get; init; }
}

// Non-blocking front door for usage logging: callers do a bounded,
// non-blocking TryWrite and move on immediately - the actual Postgres
// write happens later, batched, on UsageLogBackgroundService's own
// schedule. This matters even more here than in search-service: chat
// responses are streamed live (SSE) to a mechanic waiting on the actual
// answer, and a Postgres hiccup must never turn into extra latency or a
// failure on that path - see docs/log-dashboard.md section 3. Losing a
// handful of usage rows if the process dies between TryWrite and the next
// flush is an accepted, deliberate tradeoff for a cost dashboard, not a
// billing system of record.
public class UsageLogger
{
    private readonly Channel<UsageRecord> _channel =
        Channel.CreateBounded<UsageRecord>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
        });

    public ChannelReader<UsageRecord> Reader => _channel.Reader;

    public void Log(UsageRecord record) => _channel.Writer.TryWrite(record);
}

public class UsageLogBackgroundService : BackgroundService
{
    private const int BatchSize = 25;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    private readonly UsageLogger _logger;
    private readonly string _connectionString;
    private readonly ILogger<UsageLogBackgroundService> _diagnostics;

    public UsageLogBackgroundService(UsageLogger logger, IConfiguration configuration, ILogger<UsageLogBackgroundService> diagnostics)
    {
        _logger = logger;
        _connectionString = configuration["OUR_DB"] ?? "";
        _diagnostics = diagnostics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<UsageRecord>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            batch.Clear();
            using var timeout = new CancellationTokenSource(FlushInterval);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);

            try
            {
                while (batch.Count < BatchSize && await _logger.Reader.WaitToReadAsync(linked.Token))
                {
                    while (batch.Count < BatchSize && _logger.Reader.TryRead(out var record))
                        batch.Add(record);
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                // Flush interval elapsed with nothing (or a partial batch) read - fall through and flush below.
            }

            if (batch.Count > 0)
                await FlushAsync(batch, stoppingToken);
        }
    }

    private async Task FlushAsync(List<UsageRecord> batch, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            foreach (var record in batch)
            {
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO gemini_usage_log
                        (service_name, operation, model, prompt_tokens, completion_tokens,
                         total_tokens, is_estimated, cost_usd, session_id, raw_usage_json)
                    VALUES
                        (@serviceName, @operation, @model, @promptTokens, @completionTokens,
                         @totalTokens, @isEstimated, @costUsd, @sessionId, @rawUsageJson)
                    """, conn);
                cmd.Parameters.AddWithValue("serviceName", record.ServiceName);
                cmd.Parameters.AddWithValue("operation", record.Operation ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("model", record.Model);
                cmd.Parameters.AddWithValue("promptTokens", record.PromptTokens ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("completionTokens", record.CompletionTokens ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("totalTokens", record.TotalTokens ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("isEstimated", record.IsEstimated);
                cmd.Parameters.AddWithValue("costUsd", record.CostUsd ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("sessionId", record.SessionId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("rawUsageJson", NpgsqlTypes.NpgsqlDbType.Jsonb,
                    record.RawUsage is null ? (object)DBNull.Value : JsonSerializer.Serialize(record.RawUsage));
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        catch (Exception ex)
        {
            // Logging failures must never propagate to the request path that
            // produced these records - they're already off that path by the
            // time this runs, but a crash here would still kill the whole
            // background service. Drop the batch and keep draining.
            _diagnostics.LogWarning(ex, "Failed to write {Count} usage log rows", batch.Count);
        }
    }
}
