using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatService.Services;

// Reads config/gemini-pricing.json (mounted read-only, see docker-compose.yml)
// once at startup, cached in memory - never queried per-call. See
// docs/log-dashboard.md section 1.2. Duplicated from search-service's
// identical class - no shared library exists between the two services
// (same pattern as GeminiChatClient/QueryEmbedder already independently
// hand-rolling their own HTTP plumbing rather than sharing a base class).
public class GeminiPricing
{
    private readonly Dictionary<string, ModelPricing> _models;
    private readonly int _charactersPerToken;

    public GeminiPricing(IConfiguration configuration)
    {
        var path = configuration["GEMINI_PRICING_PATH"] ?? "";
        var json = File.ReadAllText(path);
        var doc = JsonSerializer.Deserialize<PricingFile>(json)
            ?? throw new InvalidOperationException($"Could not parse pricing file at {path}");

        _models = doc.Models;
        _charactersPerToken = doc.Estimation.CharactersPerToken;
    }

    // Gemini's embedContent endpoint never returns real token usage (see
    // docs/log-dashboard.md section 0) - this estimate is the only figure
    // available for an embedding call, and callers must mark the resulting
    // usage row is_estimated = true rather than presenting it as measured.
    // chat-service never calls embedContent today, but this stays
    // available for symmetry with search-service's identical class.
    public int EstimateTokens(string text) => (int)Math.Ceiling(text.Length / (double)_charactersPerToken);

    // Returns null (never a guessed/default price) if the model isn't in
    // the pricing file - same "never fabricate a number" principle as the
    // rest of this feature. A null cost is an honest signal to fix the
    // pricing file; a wrong cost is not.
    public decimal? CalculateEmbeddingCost(string model, int tokens)
    {
        if (!_models.TryGetValue(model, out var pricing))
            return null;
        return tokens / 1_000_000m * pricing.InputPricePerMillionTokensUsd;
    }

    public decimal? CalculateGenerateContentCost(string model, int promptTokens, int completionTokens)
    {
        if (!_models.TryGetValue(model, out var pricing))
            return null;
        return promptTokens / 1_000_000m * pricing.InputPricePerMillionTokensUsd
            + completionTokens / 1_000_000m * pricing.OutputPricePerMillionTokensUsd;
    }

    private class PricingFile
    {
        [JsonPropertyName("models")] public Dictionary<string, ModelPricing> Models { get; set; } = [];
        [JsonPropertyName("estimation")] public EstimationConfig Estimation { get; set; } = new();
    }

    private class EstimationConfig
    {
        [JsonPropertyName("characters_per_token")] public int CharactersPerToken { get; set; } = 4;
    }

    private class ModelPricing
    {
        [JsonPropertyName("input_price_per_million_tokens_usd")] public decimal InputPricePerMillionTokensUsd { get; set; }
        [JsonPropertyName("output_price_per_million_tokens_usd")] public decimal OutputPricePerMillionTokensUsd { get; set; }
    }
}
