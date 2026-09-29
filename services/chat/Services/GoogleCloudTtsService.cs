using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ChatService.Services;

public class GoogleCloudTtsService
{
    // §6.4 voice mapping — one named constant so gender choices are changed here only.
    //
    // Full voice names, exactly as /v1/voices lists them. "Algieba" on its own
    // is the character, not the voice: the family goes in the middle, so the
    // name is {locale}-Chirp3-HD-{character}. A shortened name is rejected
    // upstream, surfaces as 502 tts_upstream_failed, and the frontend drops
    // out of HD voice with the hd_voice_unavailable_toast - which reads as
    // "the voice stopped working" rather than "that voice does not exist",
    // so check the name here first.
    //
    // (The en-US catalogue also lists a bare "Algieba" alias alongside the
    // real entry. Do not use it: langCode below takes the first five
    // characters of the name, which would give "Algie".)
    //
    // Chirp3-HD, the current generation. Note it ignores speakingRate and
    // pitch in audioConfig - nothing here sets them, but a future attempt to
    // slow a procedure down would need a Neural2 or WaveNet voice instead.
    private static readonly Dictionary<string, string> VoiceMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["it"] = "it-IT-Chirp3-HD-Algieba",
            ["en"] = "en-US-Chirp3-HD-Algieba",
            ["fr"] = "fr-FR-Chirp3-HD-Algieba",
            ["pt"] = "pt-BR-Chirp3-HD-Algieba",
            ["es"] = "es-ES-Chirp3-HD-Algieba",
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
        var langCode = voice[..5]; // "it-IT" from "it-IT-Chirp3-HD-Algieba"

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
