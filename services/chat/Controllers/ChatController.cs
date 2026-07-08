using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ChatService.Models;
using ChatService.Services;

namespace ChatService.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    // camelCase to match what ASP.NET Core's MVC formatter would produce -
    // this endpoint writes to the response body manually (SSE), bypassing
    // that formatter, so it needs its own explicit options to stay
    // consistent with every other endpoint's JSON casing.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> ValidLanguages =
        new(StringComparer.OrdinalIgnoreCase) { "it", "en", "fr", "pt", "es" };

    private readonly RepairOrchestrator _orchestrator;
    private readonly GeminiChatClient _gemini;
    private readonly SessionStore _sessionStore;
    private readonly GoogleCloudTtsService _tts;

    public ChatController(
        RepairOrchestrator orchestrator,
        GeminiChatClient gemini,
        SessionStore sessionStore,
        GoogleCloudTtsService tts)
    {
        _orchestrator = orchestrator;
        _gemini = gemini;
        _sessionStore = sessionStore;
        _tts = tts;
    }

    // POST /api/chat/stream - SSE streaming. Each ChatResponse yielded by
    // RepairOrchestrator (e.g. Rule 4's confirmation message, then the
    // actual result) becomes one SSE "data:" event, flushed immediately so
    // the frontend sees the confirmation before the search even runs.
    [HttpPost("stream")]
    public async Task Stream([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        await foreach (var chunk in _orchestrator.HandleMessageAsync(request).WithCancellation(cancellationToken))
        {
            var json = JsonSerializer.Serialize(chunk, JsonOptions);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    // DELETE /api/chat/session/{sessionId} - Rule 6: clears confirmed car
    // and full session history so the next message starts completely fresh.
    [HttpDelete("session/{sessionId}")]
    public IActionResult ResetSession(string sessionId)
    {
        _sessionStore.Reset(sessionId);
        return NoContent();
    }

    // POST /api/chat/transcribe - audio transcription via Gemini. The
    // browser's actual recording format (e.g. MediaRecorder's audio/webm)
    // isn't in Gemini's officially documented supported list (wav, mp3,
    // aiff, aac, ogg, flac) - passed through as-is since there's no
    // frontend yet to confirm what it will actually send; a real gap to
    // verify once one exists, not silently assumed to work.
    [HttpPost("transcribe")]
    public async Task<IActionResult> Transcribe(IFormFile audio)
    {
        using var stream = new MemoryStream();
        await audio.CopyToAsync(stream);
        var base64 = Convert.ToBase64String(stream.ToArray());
        var mimeType = string.IsNullOrWhiteSpace(audio.ContentType) ? "audio/wav" : audio.ContentType;

        try
        {
            var transcript = await _gemini.TranscribeAsync(mimeType, base64);
            // Return plain text — frontend reads this with res.text(), not res.json().
            return Content(transcript, "text/plain");
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { error = "transcription_unavailable", detail = ex.Message });
        }
    }

    // POST /api/chat/tts - Google Cloud TTS proxy per §6.4.
    // The API key never leaves the backend. All error responses are JSON
    // (never HTML) so the frontend's fetch() always receives a parseable body.
    [HttpPost("tts")]
    public async Task<IActionResult> Tts(
        [FromBody] TtsRequest request,
        CancellationToken cancellationToken)
    {
        if (!_tts.IsConfigured)
            return StatusCode(503, new { error = "tts_not_configured" });

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "empty_text" });

        if (request.Text.Length > 2000)
            return BadRequest(new { error = "text_too_long" });

        if (!ValidLanguages.Contains(request.Language))
            return BadRequest(new { error = "unsupported_language" });

        try
        {
            var mp3 = await _tts.SynthesizeAsync(
                request.Text, request.Language.ToLowerInvariant(), cancellationToken);
            return File(mp3, "audio/mpeg");
        }
        catch (TtsUnavailableException)
        {
            return StatusCode(503, new { error = "tts_unavailable" });
        }
        catch (TtsUpstreamException)
        {
            return StatusCode(502, new { error = "tts_upstream_failed" });
        }
    }
}
