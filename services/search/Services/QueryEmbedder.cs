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

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly GeminiPricing _pricing;
    private readonly UsageLogger _usageLogger;

    public QueryEmbedder(HttpClient httpClient, IConfiguration configuration, GeminiPricing pricing, UsageLogger usageLogger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["GEMINI_API_KEY"] ?? "";
        _pricing = pricing;
        _usageLogger = usageLogger;
    }

    public async Task<float[]> EmbedAsync(string text)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:embedContent");
        request.Headers.Add("x-goog-api-key", _apiKey);
        request.Content = JsonContent.Create(new EmbedRequest
        {
            Model = $"models/{Model}",
            Content = new EmbedRequestContent { Parts = [new EmbedRequestPart { Text = text }] },
            TaskType = "RETRIEVAL_QUERY",
            OutputDimensionality = Dimensions,
        });

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<EmbedResponse>();
        var values = body?.Embedding?.Values
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
