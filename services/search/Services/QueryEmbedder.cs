using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SearchService.Services;

// Embeds search-time queries via Gemini's REST API directly - there's no
// official .NET SDK (ingestion's Python service uses google-genai). Always
// uses task_type=RETRIEVAL_QUERY (ingestion used RETRIEVAL_DOCUMENT for
// storage - see docs/EmbeddingAndGraph_Technical.md section 3 Step 1) and
// output_dimensionality=768 to match the vector(768) columns.
public class QueryEmbedder
{
    private const string Model = "gemini-embedding-001";
    private const int Dimensions = 768;

    // Same policy as ChatService.GeminiChatClient: three retries, 1s/2s/4s.
    private static readonly int[] RetryDelaysMs = [1000, 2000, 4000];

    // Worth a second attempt. 429 is quota, the rest are Google's capacity.
    // Everything else - 400 INVALID_ARGUMENT above all - is a fault in the
    // request we just built, and repeating it would fail identically.
    private static readonly HashSet<int> TransientStatuses = [429, 500, 502, 503, 504];

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly GeminiPricing _pricing;
    private readonly UsageLogger _usageLogger;
    private readonly ILogger<QueryEmbedder> _logger;

    public QueryEmbedder(HttpClient httpClient, IConfiguration configuration, GeminiPricing pricing, UsageLogger usageLogger, ILogger<QueryEmbedder> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["GEMINI_API_KEY"] ?? "";
        _pricing = pricing;
        _usageLogger = usageLogger;
        _logger = logger;
    }

    public async Task<float[]> EmbedAsync(string text)
    {
        var body = new EmbedRequest
        {
            Model = $"models/{Model}",
            Content = new EmbedRequestContent { Parts = [new EmbedRequestPart { Text = text }] },
            TaskType = "RETRIEVAL_QUERY",
            OutputDimensionality = Dimensions,
        };

        var values = await SendWithRetryAsync(body)
            ?? throw new InvalidOperationException("Gemini returned no embedding.");

        // Gemini's embedContent response never returns real token usage,
        // unlike generateContent - this is an estimate from input length,
        // never a measured fact. See docs/log-dashboard.md section 0.
        var estimatedTokens = _pricing.EstimateTokens(text);
        _usageLogger.Log(new UsageRecord
        {
            ServiceName = "search-service",
            Operation = "query_embed",
            Model = Model,
            PromptTokens = estimatedTokens,
            TotalTokens = estimatedTokens,
            IsEstimated = true,
            CostUsd = _pricing.CalculateEmbeddingCost(Model, estimatedTokens),
            RawUsage = new { estimated_from_text_length = text.Length },
        });

        return values;
    }

    // One embedContent call, retried while Gemini is the reason it failed.
    //
    // This path had no retry at all while the chat client gained one, and the
    // gap showed the first time the integration suite ran its manual tests in
    // parallel: ten 429s in a single run, five tests red, and the API
    // answering every one of those questions correctly when asked on its own.
    // Every symptom and technical search goes through here, so a rate-limit
    // burst was reaching the mechanic as "nothing found" - the worst possible
    // phrasing for "ask me again in a second".
    //
    // Retry-After is honoured when Google sends it: on a 429 the server knows
    // when it will accept us again, and guessing shorter turns one refusal
    // into four at exactly the wrong moment.
    //
    // A fresh HttpRequestMessage per attempt, because one cannot be sent twice.
    private async Task<float[]?> SendWithRetryAsync(EmbedRequest body)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:embedContent");
            request.Headers.Add("x-goog-api-key", _apiKey);
            request.Content = JsonContent.Create(body);

            int status;
            TimeSpan? retryAfter;

            try
            {
                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    if (attempt > 0)
                        _logger.LogInformation("Query embedding succeeded on attempt {Attempt}", attempt + 1);
                    return (await response.Content.ReadFromJsonAsync<EmbedResponse>())?.Embedding?.Values;
                }

                status = (int)response.StatusCode;
                retryAfter = response.Headers.RetryAfter?.Delta;
            }
            catch (Exception ex) when (
                (ex is HttpRequestException or TaskCanceledException) && attempt < RetryDelaysMs.Length)
            {
                // The connection itself failed - DNS, TLS, a dropped socket.
                // Not the request's fault, so same treatment as a 503.
                _logger.LogWarning(ex, "Query embedding could not be reached; retrying ({Attempt}/{Max})",
                    attempt + 1, RetryDelaysMs.Length);
                await Task.Delay(RetryDelaysMs[attempt]);
                continue;
            }

            if (!TransientStatuses.Contains(status) || attempt >= RetryDelaysMs.Length)
                throw new HttpRequestException($"Gemini embedContent error {status}");

            var delay = retryAfter ?? TimeSpan.FromMilliseconds(RetryDelaysMs[attempt]);
            _logger.LogWarning("Query embedding returned {Status}; retrying in {DelayMs}ms ({Attempt}/{Max})",
                status, (int)delay.TotalMilliseconds, attempt + 1, RetryDelaysMs.Length);

            await Task.Delay(delay);
        }
    }

    private class EmbedRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("content")] public EmbedRequestContent Content { get; set; } = new();
        [JsonPropertyName("taskType")] public string TaskType { get; set; } = "";
        [JsonPropertyName("outputDimensionality")] public int OutputDimensionality { get; set; }
    }

    private class EmbedRequestContent
    {
        [JsonPropertyName("parts")] public List<EmbedRequestPart> Parts { get; set; } = [];
    }

    private class EmbedRequestPart
    {
        [JsonPropertyName("text")] public string Text { get; set; } = "";
    }

    private class EmbedResponse
    {
        [JsonPropertyName("embedding")] public ContentEmbedding? Embedding { get; set; }
    }

    private class ContentEmbedding
    {
        [JsonPropertyName("values")] public float[]? Values { get; set; }
    }
}
