using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ChatService.Services;

public class GoogleCloudTtsService
{
    // §6.4 voice mapping — one named constant so gender choices are changed here only.
    private static readonly Dictionary<string, string> VoiceMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["it"] = "it-IT-Neural2-A",
            ["en"] = "en-US-Neural2-F",
            ["fr"] = "fr-FR-Neural2-A",
            ["pt"] = "pt-BR-Neural2-A",
            ["es"] = "es-ES-Neural2-A",
        };

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<GoogleCloudTtsService> _logger;
    private readonly string? _apiKey;

    public GoogleCloudTtsService(
        HttpClient http,
        ILogger<GoogleCloudTtsService> logger,
        IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["GOOGLE_CLOUD_TTS_API_KEY"];
    }

    // Returns false when the env var is absent/empty. The endpoint stays up
    // and returns 503 tts_not_configured so Web Speech keeps working.
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    // Returns MP3 bytes. Throws TtsUnavailableException on timeout/network
    // error and TtsUpstreamException when Google returns a non-2xx status.
    public async Task<byte[]> SynthesizeAsync(
        string text, string language, CancellationToken ct)
    {
        var voice = VoiceMap[language];
        var langCode = voice[..5]; // "it-IT" from "it-IT-Neural2-A"

        var payload = JsonSerializer.Serialize(new
        {
            input = new { text },
            voice = new { languageCode = langCode, name = voice },
            audioConfig = new { audioEncoding = "MP3" },
        }, JsonOpts);

        var url = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={_apiKey}";
        var sw = Stopwatch.StartNew();
        int logStatus;
        byte[] mp3;

        try
        {
            using var body = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(url, body, ct);

            sw.Stop();

            if (!resp.IsSuccessStatusCode)
            {
                logStatus = 502;
                _logger.LogWarning(
                    "TTS chars={Chars} lang={Lang} ms={Ms} status={Status} upstream={Upstream}",
                    text.Length, language, sw.ElapsedMilliseconds, logStatus,
                    (int)resp.StatusCode);
                throw new TtsUpstreamException();
            }

            var raw = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(raw);
            var b64 = doc.RootElement.GetProperty("audioContent").GetString()
                ?? throw new TtsUpstreamException();
            mp3 = Convert.FromBase64String(b64);
            logStatus = 200;
        }
        catch (TtsUpstreamException) { throw; }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
        {
            sw.Stop();
            _logger.LogWarning(
                "TTS chars={Chars} lang={Lang} ms={Ms} status=503 reason={Reason}",
                text.Length, language, sw.ElapsedMilliseconds, ex.GetType().Name);
            throw new TtsUnavailableException();
        }

        _logger.LogInformation(
            "TTS chars={Chars} lang={Lang} ms={Ms} status={Status}",
            text.Length, language, sw.ElapsedMilliseconds, logStatus);

        return mp3;
    }
}

public sealed class TtsUnavailableException() : Exception("TTS service unavailable");
public sealed class TtsUpstreamException()    : Exception("TTS upstream failed");
