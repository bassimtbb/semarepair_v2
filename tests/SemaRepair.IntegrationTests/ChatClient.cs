using System.Text;
using System.Text.Json;

namespace SemaRepair.IntegrationTests;

// Thin helper for driving POST /api/chat/stream through nginx and parsing the
// SSE "data:" events into JSON. Faithful to how the frontend actually calls it.
public static class ChatClient
{
    public sealed record ChatEvent(JsonElement Root)
    {
        public string? Phase => Root.TryGetProperty("phase", out var p) ? p.GetString() : null;
        public int CaseCount => Root.TryGetProperty("cases", out var c) && c.ValueKind == JsonValueKind.Array ? c.GetArrayLength() : 0;
        public int CarMatchCount => Root.TryGetProperty("carMatches", out var c) && c.ValueKind == JsonValueKind.Array ? c.GetArrayLength() : 0;
    }

    // Sends one message with a FRESH session id (no confirmed car unless the
    // caller supplies one) and returns every SSE event of the turn.
    public static async Task<List<ChatEvent>> SendAsync(
        string message, string sessionId, string language = "it",
        string? confirmedCarId = null, string? confirmedCodiceMotore = null, string? confirmedMarca = null)
    {
        var payload = new
        {
            sessionId,
            message,
            confirmedCarId,
            confirmedCodiceMotore,
            confirmedMarca,
            language,
        };
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await TestEnv.Http.PostAsync("/api/chat/stream", content);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();

        var events = new List<ChatEvent>();
        foreach (var chunk in body.Split("data: ", StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = chunk.Trim();
            if (trimmed.Length == 0) continue;
            events.Add(new ChatEvent(JsonDocument.Parse(trimmed).RootElement.Clone()));
        }
        return events;
    }

    public static string FreshSession(string prefix) => $"itest-{prefix}-{Guid.NewGuid():N}";
}
