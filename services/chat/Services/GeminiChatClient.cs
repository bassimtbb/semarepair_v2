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

    public GeminiChatClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["GEMINI_API_KEY"] ?? "";
    }

    // tools is omitted entirely (not sent as an empty array) when null/empty,
    // and jsonMode is the only generationConfig knob exposed - matches the
    // two distinct call shapes the orchestrator needs: a tool-routing call
    // (tools set, no JSON mode) and a formatting call (JSON mode, no tools).
    public async Task<GeminiTurn> GenerateAsync(
        IReadOnlyList<GeminiContent> contents,
        IReadOnlyList<GeminiFunctionDeclaration>? tools = null,
        string? systemInstruction = null,
        bool jsonMode = false)
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
            GenerationConfig = jsonMode ? new GenerationConfig { ResponseMimeType = "application/json" } : null,
        };
        request.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _httpClient.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini API error {(int)response.StatusCode}: {raw}");

        var parsed = JsonSerializer.Deserialize<GenerateContentResponse>(raw, JsonOptions);
        var content = parsed?.Candidates?.FirstOrDefault()?.Content
            ?? throw new InvalidOperationException("Gemini returned no candidates.");

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
        }]);
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
    }

    private class GenerateContentResponse
    {
        [JsonPropertyName("candidates")] public List<Candidate>? Candidates { get; set; }
    }

    private class Candidate
    {
        [JsonPropertyName("content")] public GeminiContent? Content { get; set; }
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
