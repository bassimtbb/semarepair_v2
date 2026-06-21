using System.Text.Json;
using ChatService.Models;

namespace ChatService.Services;

// Gemini function calling orchestration: FindCar, SearchByFaultCode,
// SearchBySymptom, SearchBySystem. See docs/SemaRepair_Architecture.md
// section 2.4 (two-call pattern) and the full decision tree in section
// 5.10 (Rules 1-13). One HandleMessageAsync call = one /api/chat/stream
// request; multi-turn state (confirmed car, history) lives in Session,
// fetched/updated via SessionStore.
public class RepairOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly GeminiChatClient _gemini;
    private readonly SessionStore _sessions;
    private readonly string _searchServiceUrl;
    private readonly string _vehicleServiceUrl;

    public RepairOrchestrator(HttpClient httpClient, GeminiChatClient gemini, SessionStore sessions, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _gemini = gemini;
        _sessions = sessions;
        _searchServiceUrl = configuration["SEARCH_SERVICE_URL"] ?? "";
        _vehicleServiceUrl = configuration["VEHICLE_SERVICE_URL"] ?? "";
    }

    public async IAsyncEnumerable<ChatResponse> HandleMessageAsync(ChatRequest request)
    {
        var session = _sessions.GetOrCreate(request.SessionId);

        // Rule 5/8: a newly confirmed (or changed) car. Yield Rule 4's
        // confirmation message immediately, and tell Gemini about it via a
        // synthetic, purely factual turn - verified live (15/15 trials
        // across two scenarios) that Gemini reliably re-issues the
        // mechanic's earlier search with the engine code filled in from
        // this alone, with no explicit re-prompt needed.
        if (!string.IsNullOrWhiteSpace(request.ConfirmedEngineCode) &&
            request.ConfirmedEngineCode != session.ConfirmedEngineCode)
        {
            var carLabel = await ConfirmCarAsync(session, request.ConfirmedEngineCode, request.ConfirmedBrand);
            yield return new ChatResponse
            {
                Phase = "chat",
                Found = false,
                Message = BuildConfirmationMessage(carLabel, request.Language),
            };

            session.History.Add(new GeminiContent
            {
                Role = "user",
                Parts = [GeminiPart.OfText(BuildConfirmationFact(request.ConfirmedEngineCode, request.ConfirmedBrand))],
            });
        }

        session.History.Add(new GeminiContent { Role = "user", Parts = [GeminiPart.OfText(request.Message)] });

        GeminiTurn? routingTurn;
        // C# disallows `yield` inside a catch block, so failures are
        // recorded here and yielded after the try/catch exits instead.
        try
        {
            routingTurn = await _gemini.GenerateAsync(
                session.History,
                tools: ToolDefinitions.All,
                systemInstruction: SystemPromptBuilder.BuildRouting(request.Language));
        }
        catch (Exception)
        {
            routingTurn = null;
        }

        if (routingTurn is null)
        {
            // Section 9.1: Gemini unavailable - no retry loop implemented
            // yet (a real gap, not an oversight: the doc specifies 3
            // retries with 1s/2s/4s backoff before this fallback message).
            yield return ServiceUnavailableResponse(request.Language);
            yield break;
        }

        session.History.Add(routingTurn.Content);

        if (routingTurn.FunctionCall is null)
        {
            // No tool needed (e.g. a greeting, or Gemini already has enough
            // context to just ask a clarifying question itself) - the
            // routing call's own text is the whole response, no formatting
            // call needed since there's no tool result to shape.
            yield return new ChatResponse { Phase = "chat", Found = false, Message = routingTurn.Text };
            yield break;
        }

        JsonElement? toolResult;
        try
        {
            toolResult = await ExecuteToolAsync(routingTurn.FunctionCall, session, request.Language);
        }
        catch (Exception)
        {
            toolResult = null;
        }

        if (toolResult is null)
        {
            // Section 9.2/9.3: Search/Vehicle Service unreachable - tell the
            // mechanic directly rather than asking Gemini to format a
            // result that doesn't exist.
            yield return ServiceUnavailableResponse(request.Language);
            yield break;
        }

        var rawResult = toolResult.Value;

        // Gemini never sees the raw tool result, anywhere - what goes into
        // History (and therefore both the formatting call below AND any
        // future routing call) is a metadata-only summary. Document/car
        // content is spliced from rawResult directly into the response
        // after the formatting call returns, never round-tripped through
        // an LLM. This was a real bug: the original design fed the full
        // result to Gemini and asked it to reproduce document fields in
        // its JSON output, and live testing found "intervento" - the
        // actual repair instructions - missing from the result.
        var resultSummary = BuildResultSummary(rawResult);
        session.History.Add(new GeminiContent
        {
            Role = "user",
            Parts = [GeminiPart.OfFunctionResponse(new GeminiFunctionResponse
            {
                Name = routingTurn.FunctionCall.Name,
                Id = routingTurn.FunctionCall.Id,
                Response = new { result = resultSummary },
            })],
        });

        // Formatting call is a one-off snapshot of the conversation so far,
        // not appended back into session.History - it uses a different
        // system instruction (output JSON shape, not tool routing) and its
        // raw JSON output isn't a real conversational turn Gemini should
        // see again later.
        var formattingTurn = await _gemini.GenerateAsync(
            session.History,
            systemInstruction: SystemPromptBuilder.BuildFormatting(request.Language),
            jsonMode: true);

        string? message = null;
        if (formattingTurn.Text is not null)
        {
            try
            {
                message = JsonSerializer.Deserialize<FormattingResult>(formattingTurn.Text, JsonOptions)?.Message;
            }
            catch (JsonException)
            {
                // Gemini didn't return valid JSON for the message field -
                // not fatal, the structured result (cases/carMatches) below
                // is unaffected since it never depended on this call.
            }
        }

        yield return BuildChatResponse(rawResult, message);
    }

    // phase/found/cases/carMatches are all derived directly from the real
    // Search/Vehicle Service response - never from Gemini's JSON output.
    private static ChatResponse BuildChatResponse(JsonElement rawResult, string? message)
    {
        if (rawResult.ValueKind == JsonValueKind.Object &&
            rawResult.TryGetProperty("cars", out var cars) &&
            cars.ValueKind == JsonValueKind.Array &&
            cars.GetArrayLength() > 0)
        {
            return new ChatResponse
            {
                Phase = "identification",
                Found = false,
                Message = message,
                CarMatches = cars.EnumerateArray().Select(ParseCarOption).ToList(),
            };
        }

        if (rawResult.ValueKind == JsonValueKind.Object &&
            rawResult.TryGetProperty("documents", out var docs) &&
            docs.ValueKind == JsonValueKind.Array &&
            docs.GetArrayLength() > 0)
        {
            return new ChatResponse
            {
                Phase = "chat",
                Found = true,
                Message = message,
                Cases = docs.EnumerateArray().Select(ParseCaseSummary).ToList(),
            };
        }

        // not_found / vague / redirected / an empty FindCar result.
        return new ChatResponse { Phase = "chat", Found = false, Message = message };
    }

    // Metadata-only view of a Search/Vehicle Service result - resultType,
    // count, and the Rule 8 transparency fields, never document/car
    // content. This is what Gemini actually sees, both in session.History
    // (so future routing turns don't see document bodies either) and as
    // input to the formatting call.
    private static object BuildResultSummary(JsonElement rawResult)
    {
        bool foundViaSharedEngine = false;
        string? sharedEngineInfo = null;
        if (rawResult.ValueKind == JsonValueKind.Object &&
            rawResult.TryGetProperty("documents", out var docs) &&
            docs.ValueKind == JsonValueKind.Array)
        {
            var first = docs.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object)
            {
                foundViaSharedEngine = first.TryGetProperty("foundViaSharedEngine", out var f) &&
                    f.ValueKind == JsonValueKind.True;
                sharedEngineInfo = GetString(first, "sharedEngineInfo");
            }
        }

        return new
        {
            resultType = GetString(rawResult, "resultType"),
            count = GetInt(rawResult, "count"),
            foundViaSharedEngine,
            sharedEngineInfo,
            validationMessage = GetString(rawResult, "validationMessage"),
            redirectedTo = GetString(rawResult, "redirectedTo"),
        };
    }

    private static CarOption ParseCarOption(JsonElement car) => new()
    {
        IdMacchina = GetString(car, "idMacchina") ?? "",
        Marca = GetString(car, "marca") ?? "",
        Modello = GetString(car, "modello") ?? "",
        Motorizzazione = GetString(car, "motorizzazione"),
        CodiceMotore = GetString(car, "codiceMotore") ?? "",
        AnnoInizio = GetInt(car, "annoInizio"),
        AnnoFine = GetInt(car, "annoFine"),
    };

    private static CaseSummary ParseCaseSummary(JsonElement doc) => new()
    {
        IdDocumento = GetString(doc, "idDocumento") ?? "",
        Sigla = GetString(doc, "siglaDocumento") ?? "",
        Impianto = GetString(doc, "impianto") ?? "",
        Dispositivo = GetString(doc, "dispositivo") ?? "",
        Anomalia = GetString(doc, "anomalia") ?? "",
        Causa = GetString(doc, "causa") ?? "",
        Intervento = GetString(doc, "intervento") ?? "",
        Procedura = GetString(doc, "procedura") ?? "",
        Nota = GetString(doc, "nota") ?? "",
        Reliability = GetInt(doc, "reliability") ?? 0,
        Language = GetString(doc, "language") ?? "",
        DtcCodes = doc.ValueKind == JsonValueKind.Object &&
            doc.TryGetProperty("dtcCodes", out var codes) &&
            codes.ValueKind == JsonValueKind.Array
                ? codes.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToList()
                : [],
    };

    private class FormattingResult
    {
        public string? Message { get; set; }
    }

    private async Task<JsonElement> ExecuteToolAsync(GeminiFunctionCall call, Session session, string language) =>
        call.Name switch
        {
            "FindCar" => await CallServiceAsync(BuildFindCarUrl(call.Args)),
            "SearchByFaultCode" => await CallServiceAsync(BuildSearchUrl("fault-code", "code", "faultCode", call.Args, session, language)),
            "SearchBySymptom" => await CallServiceAsync(BuildSearchUrl("symptom", "q", "symptom", call.Args, session, language)),
            "SearchBySystem" => await CallServiceAsync(BuildSearchUrl("system", "name", "systemName", call.Args, session, language)),
            _ => throw new InvalidOperationException($"Unknown tool: {call.Name}"),
        };

    private string BuildFindCarUrl(JsonElement args) => BuildQuery($"{_vehicleServiceUrl}/api/vehicles",
        ("brand", GetString(args, "brand")),
        ("model", GetString(args, "model")),
        ("yearFrom", GetInt(args, "yearFrom")?.ToString()),
        ("yearTo", GetInt(args, "yearTo")?.ToString()),
        ("fuel", GetString(args, "fuel")),
        ("engineCode", GetString(args, "engineCode")),
        ("kw", GetInt(args, "kw")?.ToString()));

    // Engine code/brand are deterministically taken from session state
    // when a car is confirmed, overriding whatever Gemini put in args -
    // these are simple structured values Chat Service already
    // authoritatively knows, so there's no reason to trust an LLM's echo of
    // them over the session itself (unlike the search text/fault code,
    // which only the mechanic's own words can supply).
    private string BuildSearchUrl(string endpoint, string queryParam, string argName, JsonElement args, Session session, string language)
    {
        var engineCode = session.ConfirmedEngineCode ?? GetString(args, "engineCode");
        var brand = session.ConfirmedBrand ?? GetString(args, "brand");
        return BuildQuery($"{_searchServiceUrl}/api/search/{endpoint}",
            (queryParam, GetString(args, argName)),
            ("engine", engineCode),
            ("brand", brand),
            ("lang", language));
    }

    private async Task<JsonElement> CallServiceAsync(string url)
    {
        var response = await _httpClient.GetAsync(url);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{url} returned {(int)response.StatusCode}: {raw}");
        return JsonDocument.Parse(raw).RootElement;
    }

    // Looks up the confirmed car's full details (for Rule 4's label) and
    // stores them in session - takes the first match if engineCode+brand
    // still matches more than one trim/year-range; the label is purely
    // informational text, not used for any further query scoping.
    private async Task<string> ConfirmCarAsync(Session session, string engineCode, string? brand)
    {
        session.ConfirmedEngineCode = engineCode;
        session.ConfirmedBrand = brand;

        var url = BuildQuery($"{_vehicleServiceUrl}/api/vehicles", ("engineCode", engineCode), ("brand", brand));
        try
        {
            var result = await CallServiceAsync(url);
            var car = result.GetProperty("cars").EnumerateArray().FirstOrDefault();
            if (car.ValueKind == JsonValueKind.Object)
            {
                var label = $"{car.GetProperty("marca").GetString()} {car.GetProperty("modello").GetString()}" +
                    (car.TryGetProperty("motorizzazione", out var m) && m.ValueKind == JsonValueKind.String ? $" {m.GetString()}" : "") +
                    $" ({engineCode})";
                session.ConfirmedCarLabel = label;
                return label;
            }
        }
        catch (Exception)
        {
            // Vehicle Service unreachable - confirmation still proceeds
            // (engineCode/brand are already stored above), just without a
            // friendly label this turn.
        }

        return $"{brand} ({engineCode})".Trim();
    }

    private static string BuildConfirmationFact(string engineCode, string? brand) =>
        $"Il meccanico ha confermato il veicolo. Motore: {engineCode}." + (brand is null ? "" : $" Marca: {brand}.");

    // Fixed, short template text - hardcoded per language rather than
    // asking Gemini to generate it, since it's the same sentence every
    // time (no need for an extra API call for something this simple).
    // Translations beyond Italian are my own, not verified by a native
    // speaker - flagging since this is generated/templated text rather
    // than text extracted from the mechanic's own words.
    private static string BuildConfirmationMessage(string carLabel, string language) => language switch
    {
        "en" => $"Vehicle confirmed: {carLabel}. Describe the problem or enter a fault code.",
        "fr" => $"Véhicule confirmé : {carLabel}. Décrivez le problème ou saisissez un code de panne.",
        "pt" => $"Veículo confirmado: {carLabel}. Descreva o problema ou insira um código de falha.",
        "es" => $"Vehículo confirmado: {carLabel}. Describe el problema o introduce un código de avería.",
        _ => $"Veicolo confermato: {carLabel}. Descrivi il problema o inserisci un codice guasto.",
    };

    private static ChatResponse ServiceUnavailableResponse(string language) => new()
    {
        Phase = "chat",
        Found = false,
        Message = language switch
        {
            "en" => "The service is temporarily unavailable. Please try again shortly.",
            "fr" => "Le service est temporairement indisponible. Veuillez réessayer dans un instant.",
            "pt" => "O serviço está temporariamente indisponível. Tente novamente em breve.",
            "es" => "El servicio no está disponible temporalmente. Inténtalo de nuevo en breve.",
            _ => "Il servizio è temporaneamente non disponibile. Riprova a breve.",
        },
    };

    private static string? GetString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? GetInt(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32()
            : null;

    private static string BuildQuery(string baseUrl, params (string Key, string? Value)[] parameters)
    {
        var query = string.Join("&", parameters
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));
        return query.Length == 0 ? baseUrl : $"{baseUrl}?{query}";
    }
}
