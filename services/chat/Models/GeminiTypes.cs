using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ChatService.Models;

// Wire types for Gemini's generateContent REST API - no official .NET SDK
// exists (same reasoning as Search Service's QueryEmbedder). Multi-turn
// function calling: the model's functionCall part includes an "id" that
// must be echoed back in the matching functionResponse so the model can
// map results correctly (https://ai.google.dev/gemini-api/docs/function-calling).
public class GeminiContent
{
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("parts")] public List<GeminiPart> Parts { get; set; } = [];
}

// Exactly one of Text/FunctionCall/FunctionResponse/InlineData is set per
// part - null members are omitted from the serialized JSON (see
// GeminiChatClient's JsonOptions), since Gemini rejects parts with
// multiple alternatives set.
public class GeminiPart
{
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("functionCall")] public GeminiFunctionCall? FunctionCall { get; set; }
    [JsonPropertyName("functionResponse")] public GeminiFunctionResponse? FunctionResponse { get; set; }
    [JsonPropertyName("inlineData")] public GeminiInlineData? InlineData { get; set; }

    public static GeminiPart OfText(string text) => new() { Text = text };
    public static GeminiPart OfFunctionResponse(GeminiFunctionResponse response) => new() { FunctionResponse = response };
    public static GeminiPart OfInlineData(string mimeType, string base64Data) =>
        new() { InlineData = new GeminiInlineData { MimeType = mimeType, Data = base64Data } };
}

// Audio (or other binary) data sent inline, base64-encoded - used for
// /api/chat/transcribe. See https://ai.google.dev/gemini-api/docs/audio.
public class GeminiInlineData
{
    [JsonPropertyName("mimeType")] public string MimeType { get; set; } = "";
    [JsonPropertyName("data")] public string Data { get; set; } = "";
}

public class GeminiFunctionCall
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("args")] public JsonElement Args { get; set; }
}

public class GeminiFunctionResponse
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("response")] public object Response { get; set; } = new();
}

public class GeminiFunctionDeclaration
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";

    // OpenAPI-subset JSON schema, e.g. { "type": "object", "properties":
    // {...}, "required": [...] } - built by the tool-schema definitions
    // (next build step), not by the client itself.
    [JsonPropertyName("parameters")] public JsonNode? Parameters { get; set; }
}

public class GeminiTool
{
    [JsonPropertyName("functionDeclarations")] public List<GeminiFunctionDeclaration> FunctionDeclarations { get; set; } = [];
}
