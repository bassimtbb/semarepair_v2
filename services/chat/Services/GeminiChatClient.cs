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
    // gemini-2.5-flash is closed to new API keys (404 "no longer available to
    // new users"); gemini-3.6-flash is Google's named replacement.
    private const string Model = "gemini-3.6-flash";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // docs/SemaRepair_Architecture.md section 9.1: three retries, 1s/2s/4s,
    // before the caller is told the service is unavailable.
    private static readonly int[] RetryDelaysMs = [1000, 2000, 4000];

    // Statuses worth a second attempt. 429 is quota, the rest are Google's
    // own capacity. Everything else - 400 INVALID_ARGUMENT above all - is a
    // fault in the request we just built, and repeating it would only waste
    // the mechanic's time before failing identically.
    private static readonly HashSet<int> TransientStatuses = [429, 500, 502, 503, 504];

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly GeminiPricing _pricing;
    private readonly UsageLogger _usageLogger;
    private readonly ILogger<GeminiChatClient> _logger;

    public GeminiChatClient(
        HttpClient httpClient,
        IConfiguration configuration,
        GeminiPricing pricing,
        UsageLogger usageLogger,
        ILogger<GeminiChatClient> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["GEMINI_API_KEY"] ?? "";
        _pricing = pricing;
        _usageLogger = usageLogger;
        _logger = logger;
    }

    // tools is omitted entirely (not sent as an empty array) when null/empty.
    // temperature=0 + minimal thinking on the routing call: tool selection and
    // symptom cleaning require no creativity. temperature=0 alone is not
    // sufficient because the thinking tokens are sampled independently of the
    // output temperature - the thinking chain can still vary and sometimes
    // picks "ask for clarification" instead of calling a tool. On 2.5 this was
    // thinkingBudget=0; Gemini 3 rejects thinkingBudget=0 with a bare 400
    // INVALID_ARGUMENT, and its equivalent is thinkingLevel="minimal" (thinking
    // can't be fully turned off on 3.x, so re-check routing determinism after
    // any model change - RoutingDeterminismTests). The formatting call
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
                        ThinkingConfig = disableThinking ? new ThinkingConfig { ThinkingLevel = "minimal" } : null,
                    }
                : null,
        };
        var raw = await SendWithRetryAsync(body, operation);

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

    // One generateContent call, retried while Gemini is the reason it failed.
    //
    // Measured on the deployed stack: Gemini answers 503 UNAVAILABLE - its
    // "model is overloaded" - in 300 to 900ms, in bursts, while the same
    // request succeeds moments later. A rejected call returns no
    // usageMetadata and is not billed, so an attempt that fails this way
    // costs a third of a second and nothing else. Without this loop a single
    // 503 on the routing call surfaced to the mechanic as "the service is
    // temporarily unavailable" and lost the question he had just typed.
    //
    // Worst case adds 7 seconds of waiting before the same failure he would
    // have had immediately - which is the right trade when the alternative
    // is retyping, and when most bursts clear on the first retry.
    //
    // A fresh HttpRequestMessage per attempt because one cannot be sent
    // twice; the body is built once by the caller and reserialized.
    private async Task<string> SendWithRetryAsync(GenerateContentRequest body, string operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent");
            request.Headers.Add("x-goog-api-key", _apiKey);
            request.Content = JsonContent.Create(body, options: JsonOptions);

            int status;
            string raw;

            try
            {
                using var response = await _httpClient.SendAsync(request);
                raw = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    if (attempt > 0)
                        _logger.LogInformation(
                            "Gemini {Operation} succeeded on attempt {Attempt}", operation, attempt + 1);
                    return raw;
                }

                status = (int)response.StatusCode;
            }
            catch (Exception ex) when (
                (ex is HttpRequestException or TaskCanceledException) && attempt < RetryDelaysMs.Length)
            {
                // The connection itself failed - DNS, TLS, a dropped socket.
                // Same treatment as a 503: it is not the request's fault.
                _logger.LogWarning(ex,
                    "Gemini {Operation} could not be reached; retrying in {DelayMs}ms ({Attempt}/{Max})",
                    operation, RetryDelaysMs[attempt], attempt + 1, RetryDelaysMs.Length);

                await Task.Delay(RetryDelaysMs[attempt]);
                continue;
            }

            if (!TransientStatuses.Contains(status) || attempt >= RetryDelaysMs.Length)
                throw new HttpRequestException($"Gemini API error {status}: {raw}");

            _logger.LogWarning(
                "Gemini {Operation} returned {Status}; retrying in {DelayMs}ms ({Attempt}/{Max})",
                operation, status, RetryDelaysMs[attempt], attempt + 1, RetryDelaysMs.Length);

            await Task.Delay(RetryDelaysMs[attempt]);
        }
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
                GeminiPart.OfText(TranscriptionPrompt),
                GeminiPart.OfInlineData(mimeType, base64Audio),
            ],
        }], jsonMode: true, operation: "transcription", temperature: 0);

        return ExtractTranscript(turn.Text);
    }

    // The answer must be a JSON object, not prose, and that is the whole fix.
    //
    // Asked for "only the transcribed text", Gemini mostly complies - but on
    // silence or noise it writes an apology instead, in English, and the old
    // code returned it verbatim as what the mechanic had said. Observed live:
    // "An error occurred during transcription. Please ensure the audio
    // contains clear speech and try again." appeared as his own message, and
    // the assistant then answered it.
    //
    // A refusal can no longer be mistaken for speech: it either parses as a
    // transcript or it does not, and anything that does not becomes empty.
    // The frontend already treats an empty transcript properly - onNothingHeard
    // prompts once and exits on the second - so the failure lands in the path
    // built for it instead of in the conversation.
    private const string TranscriptionPrompt =
        "Transcribe this audio exactly as spoken. Reply with JSON only: "
        + "{\"transcript\": \"<the words spoken>\"}. "
        + "Do not translate, correct, summarise or comment. "
        + "If the audio contains no intelligible speech, reply {\"transcript\": \"\"} "
        + "- never explain, never apologise, never describe the audio.";

    private static string ExtractTranscript(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("transcript", out var t)
                && t.ValueKind == JsonValueKind.String
                    ? t.GetString() ?? ""
                    : "";
        }
        catch (JsonException)
        {
            // Not JSON at all: the model ignored the contract. Whatever it
            // wrote, it is not what the mechanic said.
            return "";
        }
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
        [JsonPropertyName("thinkingLevel")] public string ThinkingLevel { get; set; } = "";
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
