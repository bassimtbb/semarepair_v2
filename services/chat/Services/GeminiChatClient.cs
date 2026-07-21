using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChatService.Models;

namespace ChatService.Services;

// Low-level wrapper around Gemini's generateContent REST API - no official
// .NET SDK exists (same reasoning as Search Service's QueryEmbedder). Owns
// only the wire protocol: building a request from contents/tools/JSON-mode
// and unpacking the model's reply into a GeminiTurn. Conversation history,
// tool dispatch, and the two-call (tool-routing, then JSON-format) pattern
// from docs/SemaRepair_Architecture.md section 2.4 all live in
// RepairOrchestrator, not here.
public class GeminiChatClient
{
    private const string Model = "gemini-2.5-flash";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly GeminiPricing _pricing;
    private readonly UsageLogger _usageLogger;

    public GeminiChatClient(HttpClient httpClient, IConfiguration configuration, GeminiPricing pricing, UsageLogger usageLogger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["GEMINI_API_KEY"] ?? "";
        _pricing = pricing;
        _usageLogger = usageLogger;
    }

    // tools is omitted entirely (not sent as an empty array) when null/empty.
    // temperature=0 + thinkingBudget=0 on the routing call: tool selection and
    // symptom cleaning require no creativity. temperature=0 alone is not
    // sufficient for gemini-2.5-flash because the thinking tokens are sampled
    // independently of the output temperature - the thinking chain can still
    // vary and sometimes picks "ask for clarification" instead of calling a
    // tool. Disabling thinking (thinkingBudget=0) removes that source of
    // non-determinism and makes routing fully greedy. The formatting call
    // does NOT get these settings - it generates conversational prose where
    // slight variation is harmless and thinking helps quality.
    // temperature=null / disableThinking=false omits those fields, using the
    // model defaults. operation/sessionId carry no request behavior - they
    // only label the usage row (docs/log-dashboard.md section 1.1).
    public async Task<GeminiTurn> GenerateAsync(
        IReadOnlyList<GeminiContent> contents,
        IReadOnlyList<GeminiFunctionDeclaration>? tools = null,
        string? systemInstruction = null,
        bool jsonMode = false,
        string operation = "unspecified",
        string? sessionId = null,
        float? temperature = null,
        bool disableThinking = false)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent");
        request.Headers.Add("x-goog-api-key", _apiKey);

        var body = new GenerateContentRequest
        {
            Contents = contents,
            Tools = tools is { Count: > 0 } ? [new GeminiTool { FunctionDeclarations = tools.ToList() }] : null,
            SystemInstruction = systemInstruction is null
                ? null
                : new GeminiContent { Parts = [GeminiPart.OfText(systemInstruction)] },
            GenerationConfig = (jsonMode || temperature.HasValue || disableThinking)
                ? new GenerationConfig
                    {
                        ResponseMimeType = jsonMode ? "application/json" : null,
                        Temperature = temperature,
                        ThinkingConfig = disableThinking ? new ThinkingConfig { ThinkingBudget = 0 } : null,
                    }
                : null,
        };
        request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini API error {(int)response.StatusCode}: {raw}");

        var parsed = JsonSerializer.Deserialize<GenerateContentResponse>(raw, JsonOptions);
        var content = parsed?.Candidates?.FirstOrDefault()?.Content
            ?? throw new InvalidOperationException("Gemini returned no candidates.");

        // generateContent always returns real usageMetadata, unlike
        // embedContent (docs/log-dashboard.md section 0) - these are
        // measured tokens, not an estimate, so is_estimated stays false.
        //
        // completionTokens folds candidatesTokenCount (the visible reply)
        // together with thoughtsTokenCount (2.5 Flash's hidden "thinking"
        // tokens) - confirmed via a real direct API call that
        // promptTokenCount + candidatesTokenCount alone doesn't add up to
        // totalTokenCount; the gap is exactly thoughtsTokenCount, and
        // Google bills thinking tokens at the same output rate as regular
        // output (gemini-pricing.json's own note). Folding them here keeps
        // this column consistent with both totalTokenCount and the cost
        // figure computed from it, while raw_usage_json still preserves
        // the verbatim, un-folded breakdown.
        if (parsed?.UsageMetadata is { } usage)
        {
            var completionTokens = usage.CandidatesTokenCount + usage.ThoughtsTokenCount;
            _usageLogger.Log(new UsageRecord
            {
                ServiceName = "chat-service",
                Operation = operation,
                Model = Model,
                PromptTokens = usage.PromptTokenCount,
                CompletionTokens = completionTokens,
                TotalTokens = usage.TotalTokenCount,
                IsEstimated = false,
                CostUsd = _pricing.CalculateGenerateContentCost(Model, usage.PromptTokenCount, completionTokens),
                SessionId = sessionId,
                RawUsage = usage,
            });
        }

        return new GeminiTurn(content);
    }

    // /api/chat/transcribe - a one-off, history-free call (no tools, no
    // JSON mode): just audio in, transcript text out. Deliberately does not
    // ask Gemini to translate - the mechanic's spoken language should pass
    // straight through, same principle as the routing call never
    // translating extracted symptom text.
    public async Task<string> TranscribeAsync(string mimeType, string base64Audio)
    {
        var turn = await GenerateAsync([new GeminiContent
        {
            Role = "user",
            Parts =
            [
                GeminiPart.OfText("Transcribe this audio exactly as spoken. Return ONLY the transcribed text, with no commentary, translation, or formatting."),
                GeminiPart.OfInlineData(mimeType, base64Audio),
            ],
        }], operation: "transcription");
        return turn.Text ?? "";
    }

    private class GenerateContentRequest
    {
        [JsonPropertyName("contents")] public IReadOnlyList<GeminiContent> Contents { get; set; } = [];
        [JsonPropertyName("tools")] public List<GeminiTool>? Tools { get; set; }

        // No "role" on systemInstruction - it's a plain Content{parts} object,
        // unlike conversation turns which always carry a role.
        [JsonPropertyName("systemInstruction")] public GeminiContent? SystemInstruction { get; set; }
        [JsonPropertyName("generationConfig")] public GenerationConfig? GenerationConfig { get; set; }
    }

    private class GenerationConfig
    {
        [JsonPropertyName("responseMimeType")] public string? ResponseMimeType { get; set; }
        [JsonPropertyName("temperature")] public float? Temperature { get; set; }
        [JsonPropertyName("thinkingConfig")] public ThinkingConfig? ThinkingConfig { get; set; }
    }

    private class ThinkingConfig
    {
        [JsonPropertyName("thinkingBudget")] public int ThinkingBudget { get; set; }
    }

    private class GenerateContentResponse
    {
        [JsonPropertyName("candidates")] public List<Candidate>? Candidates { get; set; }
        [JsonPropertyName("usageMetadata")] public UsageMetadata? UsageMetadata { get; set; }
    }

    private class Candidate
    {
        [JsonPropertyName("content")] public GeminiContent? Content { get; set; }
    }

    // Real, measured token counts Gemini's generateContent always returns
    // alongside the response - see docs/log-dashboard.md section 0. Public
    // so it can be stored verbatim as UsageRecord.RawUsage (the raw fact,
    // not just the derived columns - see that record's own comment).
    public class UsageMetadata
    {
        [JsonPropertyName("promptTokenCount")] public int PromptTokenCount { get; set; }
        [JsonPropertyName("candidatesTokenCount")] public int CandidatesTokenCount { get; set; }
        [JsonPropertyName("thoughtsTokenCount")] public int ThoughtsTokenCount { get; set; }
        [JsonPropertyName("totalTokenCount")] public int TotalTokenCount { get; set; }
    }
}

// A model turn, with the raw Content (needed verbatim in conversation
// history for multi-turn function calling - re-sending it is what lets the
// model see its own prior function call) plus convenience accessors.
public class GeminiTurn
{
    public GeminiContent Content { get; }

    public GeminiTurn(GeminiContent content)
    {
        Content = content;
    }

    public string? Text =>
        Content.Parts.Any(p => p.Text is not null)
            ? string.Concat(Content.Parts.Where(p => p.Text is not null).Select(p => p.Text))
            : null;

    public GeminiFunctionCall? FunctionCall =>
        Content.Parts.FirstOrDefault(p => p.FunctionCall is not null)?.FunctionCall;
}
