using Npgsql;
using SearchService.Models;

namespace SearchService.Services;

// Read-only aggregation over gemini_usage_log, for the usage dashboard
// (docs/log-dashboard.md section 5). Deliberately centralized here rather
// than split across chat-service/search-service/ingestion-resx (each of
// which only ever writes its own rows) - one place for aggregation SQL,
// not three. Raw parameterized SQL, no ORM - same convention as
// GraphSearchService/DocumentContentService.
public class UsageQueryService
{
    private readonly string _connectionString;

    public UsageQueryService(IConfiguration configuration)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
    }

    private NpgsqlConnection Connect() => new(_connectionString);

    public async Task<UsageSummaryResponse> GetSummaryAsync(DateTimeOffset? from, DateTimeOffset? to)
    {
        await using var conn = Connect();
        await conn.OpenAsync();

        var summary = new UsageSummaryResponse();

        await using (var cmd = new NpgsqlCommand("""
            SELECT COALESCE(SUM(cost_usd), 0), COALESCE(SUM(total_tokens), 0), COUNT(*)
            FROM gemini_usage_log
            WHERE (@from::timestamptz IS NULL OR occurred_at >= @from)
              AND (@to::timestamptz IS NULL OR occurred_at <= @to)
            """, conn))
        {
            AddRangeParams(cmd, from, to);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                summary.TotalCostUsd = reader.GetDecimal(0);
                summary.TotalTokens = reader.GetInt64(1);
                summary.TotalCalls = reader.GetInt64(2);
            }
        }

        summary.ByModel = await GetBreakdownAsync(conn, "model", from, to);
        summary.ByService = await GetBreakdownAsync(conn, "service_name", from, to);

        return summary;
    }

    // groupColumn is never user input - always one of the two literal
    // calls above - so interpolating it directly into the SQL (Postgres
    // doesn't allow GROUP BY/column names as bind parameters) is safe.
    private static async Task<List<UsageBreakdown>> GetBreakdownAsync(
        NpgsqlConnection conn, string groupColumn, DateTimeOffset? from, DateTimeOffset? to)
    {
        var results = new List<UsageBreakdown>();
        await using var cmd = new NpgsqlCommand($"""
            SELECT {groupColumn}, COALESCE(SUM(cost_usd), 0), COALESCE(SUM(total_tokens), 0), COUNT(*)
            FROM gemini_usage_log
            WHERE (@from::timestamptz IS NULL OR occurred_at >= @from)
              AND (@to::timestamptz IS NULL OR occurred_at <= @to)
            GROUP BY {groupColumn}
            ORDER BY 2 DESC
            """, conn);
        AddRangeParams(cmd, from, to);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new UsageBreakdown
            {
                Key = reader.GetString(0),
                CostUsd = reader.GetDecimal(1),
                Tokens = reader.GetInt64(2),
                Calls = reader.GetInt64(3),
            });
        }
        return results;
    }

    // bucket is validated by the caller (UsageController) against an
    // allowlist of "hour"/"day" before reaching here - date_trunc's first
    // argument can't be a normal bind parameter in every Postgres version,
    // but it's a plain text argument to a function call, not a SQL
    // keyword/identifier position, so passing it as @bucket is safe once
    // validated.
    public async Task<List<UsageTimeSeriesPoint>> GetTimeSeriesAsync(string bucket, DateTimeOffset? from, DateTimeOffset? to)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT date_trunc(@bucket, occurred_at) AS bucket_start,
                   COALESCE(SUM(cost_usd), 0), COALESCE(SUM(total_tokens), 0), COUNT(*)
            FROM gemini_usage_log
            WHERE (@from::timestamptz IS NULL OR occurred_at >= @from)
              AND (@to::timestamptz IS NULL OR occurred_at <= @to)
            GROUP BY bucket_start
            ORDER BY bucket_start
            """, conn);
        cmd.Parameters.AddWithValue("bucket", bucket);
        AddRangeParams(cmd, from, to);

        var results = new List<UsageTimeSeriesPoint>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new UsageTimeSeriesPoint
            {
                BucketStart = reader.GetDateTime(0),
                CostUsd = reader.GetDecimal(1),
                Tokens = reader.GetInt64(2),
                Calls = reader.GetInt64(3),
            });
        }
        return results;
    }

    public async Task<UsageLogPage> GetLogsAsync(
        string? service, string? model, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, occurred_at, service_name, operation, model, prompt_tokens,
                   completion_tokens, total_tokens, is_estimated, cost_usd, session_id
            FROM gemini_usage_log
            WHERE (@service::text IS NULL OR service_name = @service)
              AND (@model::text IS NULL OR model = @model)
              AND (@from::timestamptz IS NULL OR occurred_at >= @from)
              AND (@to::timestamptz IS NULL OR occurred_at <= @to)
            ORDER BY occurred_at DESC
            LIMIT @limit OFFSET @offset
            """, conn);
        cmd.Parameters.AddWithValue("service", service ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("model", model ?? (object)DBNull.Value);
        AddRangeParams(cmd, from, to);
        // Fetches one extra row to determine HasMore without a separate
        // COUNT(*) query - trimmed back to pageSize before returning.
        cmd.Parameters.AddWithValue("limit", pageSize + 1);
        cmd.Parameters.AddWithValue("offset", page * pageSize);

        var entries = new List<UsageLogEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new UsageLogEntry
            {
                Id = reader.GetInt64(0),
                OccurredAt = reader.GetDateTime(1),
                ServiceName = reader.GetString(2),
                Operation = reader.IsDBNull(3) ? null : reader.GetString(3),
                Model = reader.GetString(4),
                PromptTokens = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                CompletionTokens = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                TotalTokens = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                IsEstimated = reader.GetBoolean(8),
                CostUsd = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                SessionId = reader.IsDBNull(10) ? null : reader.GetString(10),
            });
        }

        var hasMore = entries.Count > pageSize;
        if (hasMore)
            entries.RemoveAt(entries.Count - 1);

        return new UsageLogPage { Entries = entries, HasMore = hasMore };
    }

    private static void AddRangeParams(NpgsqlCommand cmd, DateTimeOffset? from, DateTimeOffset? to)
    {
        cmd.Parameters.AddWithValue("from", from ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("to", to ?? (object)DBNull.Value);
    }
}
