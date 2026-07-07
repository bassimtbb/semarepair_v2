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
    private readonly ILogger<RepairOrchestrator> _logger;
    private readonly string _searchServiceUrl;
    private readonly string _vehicleServiceUrl;

    public RepairOrchestrator(HttpClient httpClient, GeminiChatClient gemini, SessionStore sessions, IConfiguration configuration, ILogger<RepairOrchestrator> logger)
    {
        _httpClient = httpClient;
        _gemini = gemini;
        _sessions = sessions;
        _logger = logger;
        _searchServiceUrl = configuration["SEARCH_SERVICE_URL"] ?? "";
        _vehicleServiceUrl = configuration["VEHICLE_SERVICE_URL"] ?? "";
    }

    public async IAsyncEnumerable<ChatResponse> HandleMessageAsync(ChatRequest request)
    {
        var session = _sessions.GetOrCreate(request.SessionId);
        _logger.LogInformation("[history-check] session={SessionId} historyOnArrival={Count}", request.SessionId, session.History.Count);

        // Rule 5/8: a newly confirmed (or changed) car. Compare by
        // ConfirmedCarId first - ConfirmedCodiceMotore alone can be
        // unchanged while the mechanic picks a *different* car (e.g.
        // switching between two IVECO Daily III trims that share engine
        // 8140.43S), which a plain codiceMotore comparison would miss
        // entirely. Only fall back to comparing codiceMotore when no id
        // was supplied at all.
        var confirmingNewCar = !string.IsNullOrWhiteSpace(request.ConfirmedCarId)
            ? request.ConfirmedCarId != session.ConfirmedCarId
            : !string.IsNullOrWhiteSpace(request.ConfirmedCodiceMotore) && request.ConfirmedCodiceMotore != session.ConfirmedCodiceMotore;

        if (confirmingNewCar)
        {
            // No hardcoded "Veicolo confermato: ..." yield here anymore -
            // it was always redundant: the routing call below (which runs
            // unconditionally right after, processing request.Message with
            // this synthetic fact already in History) produces its own
            // natural acknowledgment, or replays the original search
            // (Rule 7) when one was pending. The frontend's own synthetic
            // "Confermo il veicolo: ..." user message is suppressed the
            // same way (ChatStore.confirmCar), for the same reason - two
            // robotic confirmation bubbles before Gemini's real reply.
            // ConfirmCarAsync's return value (a label) is no longer needed
            // here for that reason, only its side effect (storing the
            // confirmed car in session).
            await ConfirmCarAsync(session, request.ConfirmedCarId, request.ConfirmedCodiceMotore, request.ConfirmedMarca);

            // Built from session's now-authoritative values (resolved by
            // ConfirmCarAsync from the specific car id when given), not
            // the raw request fields - keeps Gemini's fact turn consistent
            // with whatever was actually confirmed.
            session.History.Add(new GeminiContent
            {
                Role = "user",
                Parts = [GeminiPart.OfText(BuildConfirmationFact(session.ConfirmedCodiceMotore, session.ConfirmedMarca))],
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
                systemInstruction: SystemPromptBuilder.BuildRouting(request.Language),
                operation: "routing",
                sessionId: request.SessionId);
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
        // Only meaningful for SearchBySymptom (the only tool that ever sets
        // lowConfidenceMatch - see SearchController.SymptomWithCarAsync) -
        // the mechanic's affirmative reply to a previous low-confidence
        // offer, decided by Gemini's own routing call from conversation
        // context, same as how Rule 7's replay decision works.
        var lowConfidenceConfirmed = routingTurn.FunctionCall.Name == "SearchBySymptom" &&
            GetBool(routingTurn.FunctionCall.Args, "confirmLowConfidenceMatch");

        // Two-distinct-faults handling: when the mechanic describes two
        // separate problems in one message, the routing call keeps the
        // discarded one in "secondarySymptom" instead of dropping it (see
        // ToolDefinitions.cs/SystemPromptBuilder.BuildRouting). If the
        // first (more specific) symptom finds nothing, automatically
        // re-search with the second one - deterministically in C#, no
        // extra Gemini round-trip - and use THAT result for the rest of
        // this turn. Deliberately gated on a literal "not_found", never on
        // a low-confidence match: those already have their own ask-first
        // flow (Rule 8b/8c) and shouldn't be conflated with this one.
        string? primarySymptomText = null;
        string? secondarySymptomText = null;
        var secondarySymptomTried = false;
        if (routingTurn.FunctionCall.Name == "SearchBySymptom" &&
            IsNotFoundResult(rawResult) &&
            GetString(routingTurn.FunctionCall.Args, "secondarySymptom") is { Length: > 0 } secondary)
        {
            primarySymptomText = GetString(routingTurn.FunctionCall.Args, "symptom");
            secondarySymptomText = secondary;
            secondarySymptomTried = true;
            try
            {
                rawResult = await CallServiceAsync(BuildSymptomSearchUrl(secondary, routingTurn.FunctionCall.Args, session, request.Language));
            }
            catch (Exception)
            {
                // Secondary search itself failed (service hiccup, not "no
                // match") - keep the original not_found result rather than
                // losing the turn; Rule 9b still fires correctly since
                // rawResult's resultType is still "not_found".
            }
        }

        var resultSummary = BuildResultSummary(rawResult, lowConfidenceConfirmed, secondarySymptomTried, primarySymptomText, secondarySymptomText);
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
            jsonMode: true,
            operation: "formatting",
            sessionId: request.SessionId);

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

        yield return BuildChatResponse(rawResult, message, routingTurn.FunctionCall, request.Language, lowConfidenceConfirmed);
    }

    // phase/found/cases/carMatches are all derived directly from the real
    // Search/Vehicle Service response - never from Gemini's JSON output.
    private static ChatResponse BuildChatResponse(
        JsonElement rawResult, string? message, GeminiFunctionCall call, string language, bool lowConfidenceConfirmed)
    {
        if (rawResult.ValueKind == JsonValueKind.Object &&
            rawResult.TryGetProperty("cars", out var cars) &&
            cars.ValueKind == JsonValueKind.Array &&
            cars.GetArrayLength() > 0)
        {
            // Unchanged from before this session's FindCar-not-found work -
            // also correctly handles Search Service's own car-selection
            // responses (Rule 1/2), not just FindCar's.
            return new ChatResponse
            {
                Phase = "identification",
                Found = false,
                Message = message,
                CarMatches = cars.EnumerateArray().Select(ParseCarOption).ToList(),
            };
        }

        // Real bug found via live testing: Search Service's own response
        // shape (fault-code/symptom/system) always includes an empty
        // "cars": [] placeholder field *alongside* a real "documents"
        // array - checking for "cars" presence alone (regardless of
        // call.Name) made this branch wrongly fire for a SUCCESSFUL
        // fault-code search, discarding the real found document and
        // replacing it with a fabricated "vehicle not found" message.
        // Gating on call.Name == "FindCar" is what actually distinguishes
        // "this is Vehicle Service's empty result" from "Search Service's
        // harmless empty placeholder field" - shape alone isn't enough.
        if (call.Name == "FindCar" &&
            rawResult.ValueKind == JsonValueKind.Object &&
            rawResult.TryGetProperty("cars", out _))
        {
            // FindCar matched nothing - build the not-found message
            // deterministically from VehicleResponse's real
            // suggestedYearFrom/suggestedYearTo facts (set only when
            // Vehicle Service's own fallback query found the brand+model
            // for *some* year range - see VehicleSearchService), never
            // from Gemini's free-text formatting call. Same fidelity
            // principle as document content/car identity elsewhere in this
            // build - the formatting call still runs (it doesn't know
            // which tool produced rawResult), its "message" is just
            // discarded here.
            return new ChatResponse
            {
                Phase = "chat",
                Found = false,
                Message = BuildVehicleNotFoundMessage(
                    BuildVehicleLabel(GetString(call.Args, "brand"), GetString(call.Args, "model"), language),
                    BuildYearLabel(GetInt(call.Args, "yearFrom"), GetInt(call.Args, "yearTo")),
                    GetInt(rawResult, "suggestedYearFrom"),
                    GetInt(rawResult, "suggestedYearTo"),
                    language),
            };
        }

        if (rawResult.ValueKind == JsonValueKind.Object &&
            rawResult.TryGetProperty("documents", out var docs) &&
            docs.ValueKind == JsonValueKind.Array &&
            docs.GetArrayLength() > 0)
        {
            var first = docs.EnumerateArray().First();
            var isLowConfidence = first.ValueKind == JsonValueKind.Object &&
                first.TryGetProperty("lowConfidenceMatch", out var lc) && lc.ValueKind == JsonValueKind.True;

            // Real bug fix (see progress.md section 6.20): a low-confidence
            // match used to be shown immediately, with only the chat text
            // disclaiming it - the document card itself looked just as
            // confident as a real match. Now it's withheld entirely until
            // the mechanic explicitly says they want to see it anyway
            // (lowConfidenceConfirmed, decided by Gemini's routing call from
            // conversation context) - the formatting call's message (Rule
            // 8b) is the whole response this turn.
            if (isLowConfidence && !lowConfidenceConfirmed)
                return new ChatResponse { Phase = "chat", Found = false, Message = message };

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
    // count, and the Rule 8/8b transparency fields, never document/car
    // content. This is what Gemini actually sees, both in session.History
    // (so future routing turns don't see document bodies either) and as
    // input to the formatting call.
    private static bool IsNotFoundResult(JsonElement rawResult) =>
        GetString(rawResult, "resultType") == "not_found";

    private static object BuildResultSummary(
        JsonElement rawResult, bool lowConfidenceConfirmed,
        bool secondarySymptomTried, string? primarySymptomText, string? secondarySymptomText)
    {
        bool foundViaSharedEngine = false;
        string? sharedEngineInfo = null;
        bool lowConfidenceMatch = false;
        string? lowConfidenceReason = null;
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
                lowConfidenceMatch = first.TryGetProperty("lowConfidenceMatch", out var lc) &&
                    lc.ValueKind == JsonValueKind.True;
                lowConfidenceReason = GetString(first, "lowConfidenceReason");
            }
        }

        return new
        {
            resultType = GetString(rawResult, "resultType"),
            count = GetInt(rawResult, "count"),
            foundViaSharedEngine,
            sharedEngineInfo,
            lowConfidenceMatch,
            lowConfidenceConfirmed,
            lowConfidenceReason,
            secondarySymptomTried,
            primarySymptomText,
            secondarySymptomText,
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
        Alimentazione = GetString(car, "alimentazione"),
        AnnoInizio = GetInt(car, "annoInizio"),
        AnnoFine = GetInt(car, "annoFine"),
        Kw = GetInt(car, "kw"),
        Cavalli = GetInt(car, "cavalli"),
    };

    private static CaseSummary ParseCaseSummary(JsonElement doc) => new()
    {
        IdDocumento = GetString(doc, "idDocumento") ?? "",
        Sigla = GetString(doc, "siglaDocumento") ?? "",
        Titolo = GetString(doc, "titolo") ?? "",
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
                ? codes.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object).Select(c => new FaultCodeInfo
                    {
                        Code = GetString(c, "code") ?? "",
                        Description = GetString(c, "description"),
                    }).ToList()
                : [],
        FoundViaSharedEngine = doc.TryGetProperty("foundViaSharedEngine", out var fvse) &&
            fvse.ValueKind == JsonValueKind.True,
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

    // Query-string keys sent to Vehicle Service are Italian
    // (marca/modello/annoInizio/annoFine/alimentazione/motorizzazione/codiceMotore) -
    // Vehicle Service's own deliberate deviation from the architecture
    // doc's English query params (see VehicleQuery.cs). Gemini's own
    // function-call argument names below (the second argument to
    // GetString/GetInt) are unrelated and stay English, matching
    // ToolDefinitions.cs - only the outgoing HTTP query key changes.
    //
    // motorizzazione/engineCode map to two different Gemini args
    // (ToolDefinitions.FindCar) on purpose - conflating them into one
    // "engineCode" param was a real bug: a mechanic's free-text engine
    // label ("1.5 TDCi 8v") landed in Vehicle Service's exact-match
    // CodiceMotore filter, silently matching zero rows instead of the
    // real ones.
    private string BuildFindCarUrl(JsonElement args) => BuildQuery($"{_vehicleServiceUrl}/api/vehicles",
        ("marca", GetString(args, "brand")),
        ("modello", GetString(args, "model")),
        ("annoInizio", GetInt(args, "yearFrom")?.ToString()),
        ("annoFine", GetInt(args, "yearTo")?.ToString()),
        ("alimentazione", GetString(args, "fuel")),
        ("motorizzazione", GetString(args, "motorizzazione")),
        ("codiceMotore", GetString(args, "engineCode")),
        ("kw", GetInt(args, "kw")?.ToString()));

    // Engine code/brand are deterministically taken from session state
    // when a car is confirmed, overriding whatever Gemini put in args -
    // these are simple structured values Chat Service already
    // authoritatively knows, so there's no reason to trust an LLM's echo of
    // them over the session itself (unlike the search text/fault code,
    // which only the mechanic's own words can supply).
    //
    // Query-string keys sent to Search Service are Italian
    // (codiceMotore/marca) - Search Service's own deliberate deviation
    // from the architecture doc's English query params (see
    // SearchRequest.cs), matching Vehicle Service's convention. Gemini's
    // own function-call argument names below (the second argument to
    // GetString) are unrelated and stay English, matching
    // ToolDefinitions.cs - only the outgoing HTTP query key changes.
    private string BuildSearchUrl(string endpoint, string queryParam, string argName, JsonElement args, Session session, string language)
    {
        var codiceMotore = session.ConfirmedCodiceMotore ?? GetString(args, "engineCode");
        var marca = session.ConfirmedMarca ?? GetString(args, "brand");
        return BuildQuery($"{_searchServiceUrl}/api/search/{endpoint}",
            (queryParam, GetString(args, argName)),
            ("codiceMotore", codiceMotore),
            ("marca", marca),
            ("lang", language));
    }

    // Re-runs a symptom search with literal text rather than Gemini's own
    // call args - used only for the secondary-symptom retry above, where
    // the text to search ("secondarySymptom") is separate from the
    // original call's "symptom" argument. engineCode/brand fallback
    // mirrors BuildSearchUrl exactly (session's confirmed values take
    // priority; originalArgs is the FIRST call's args, since Gemini never
    // gave a separate engineCode/brand for the discarded symptom).
    private string BuildSymptomSearchUrl(string symptomText, JsonElement originalArgs, Session session, string language)
    {
        var codiceMotore = session.ConfirmedCodiceMotore ?? GetString(originalArgs, "engineCode");
        var marca = session.ConfirmedMarca ?? GetString(originalArgs, "brand");
        return BuildQuery($"{_searchServiceUrl}/api/search/symptom",
            ("q", symptomText), ("codiceMotore", codiceMotore), ("marca", marca), ("lang", language));
    }

    private async Task<JsonElement> CallServiceAsync(string url)
    {
        var response = await _httpClient.GetAsync(url);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{url} returned {(int)response.StatusCode}: {raw}");
        return JsonDocument.Parse(raw).RootElement;
    }

    // Resolves the confirmed car and stores it in session. carId (the
    // mechanic's actual card click - idMacchina) is the only unambiguous
    // path: codiceMotore+marca alone can match several trims at once
    // (e.g. IVECO Daily III 35C-13/40C-13/45C-13/50C-13 all share engine
    // 8140.43S) - live testing confirmed clicking "40C-13" was silently
    // resolved to "35C-13" because the old codiceMotore+marca lookup had
    // no way to disambiguate and just took whatever row came back first.
    // codiceMotore/marca are kept only as a fallback for callers that
    // don't have a specific car id - that path can still be ambiguous,
    // but it's no longer the primary one.
    private async Task<string> ConfirmCarAsync(Session session, string? carId, string? codiceMotore, string? marca)
    {
        if (!string.IsNullOrWhiteSpace(carId))
        {
            try
            {
                var car = await CallServiceAsync($"{_vehicleServiceUrl}/api/vehicles/{Uri.EscapeDataString(carId)}");
                if (car.ValueKind == JsonValueKind.Object && car.TryGetProperty("idMacchina", out _))
                {
                    return StoreConfirmedCar(session, carId, car);
                }
            }
            catch (Exception)
            {
                // Vehicle Service unreachable, or the id no longer exists -
                // fall through to the codiceMotore/marca fallback below
                // rather than failing the whole confirmation outright.
            }
        }

        if (string.IsNullOrWhiteSpace(codiceMotore))
        {
            // No id and no engine code at all - nothing to confirm against.
            session.ConfirmedCarId = carId;
            session.ConfirmedMarca = marca;
            return marca ?? "";
        }

        var url = BuildQuery($"{_vehicleServiceUrl}/api/vehicles", ("codiceMotore", codiceMotore), ("marca", marca));
        try
        {
            var result = await CallServiceAsync(url);
            var car = result.GetProperty("cars").EnumerateArray().FirstOrDefault();
            if (car.ValueKind == JsonValueKind.Object)
            {
                var resolvedId = car.TryGetProperty("idMacchina", out var idProp) ? idProp.GetString() : null;
                return StoreConfirmedCar(session, resolvedId, car);
            }
        }
        catch (Exception)
        {
            // Vehicle Service unreachable - confirmation still proceeds
            // below, just without a friendly label this turn.
        }

        session.ConfirmedCarId = carId;
        session.ConfirmedCodiceMotore = codiceMotore;
        session.ConfirmedMarca = marca;
        var fallbackLabel = $"{marca} ({codiceMotore})".Trim();
        session.ConfirmedCarLabel = fallbackLabel;
        return fallbackLabel;
    }

    private static string StoreConfirmedCar(Session session, string? carId, JsonElement car)
    {
        var codiceMotore = car.GetProperty("codiceMotore").GetString();
        var marca = car.GetProperty("marca").GetString();
        var label = $"{marca} {car.GetProperty("modello").GetString()}" +
            (car.TryGetProperty("motorizzazione", out var m) && m.ValueKind == JsonValueKind.String ? $" {m.GetString()}" : "") +
            $" ({codiceMotore})";

        session.ConfirmedCarId = carId;
        session.ConfirmedCodiceMotore = codiceMotore;
        session.ConfirmedMarca = marca;
        session.ConfirmedCarLabel = label;
        return label;
    }

    private static string BuildConfirmationFact(string? codiceMotore, string? marca) =>
        "Il meccanico ha confermato il veicolo." +
        (codiceMotore is null ? "" : $" Motore: {codiceMotore}.") +
        (marca is null ? "" : $" Marca: {marca}.");


    // Distinct from the generic case below: brand+model genuinely exist in
    // gup_rows for *some* year range (suggestedYearFrom/To are non-null,
    // set only by Vehicle Service's own fallback query - never invented
    // here), so the mechanic gets the real range instead of a blank "not
    // found." requestedYearLabel is built from the mechanic's own
    // yearFrom/yearTo args, which is guaranteed non-null whenever a
    // suggestion exists - Vehicle Service only runs its fallback query
    // when the original request had a year filter at all.
    private static string BuildVehicleNotFoundMessage(
        string vehicleLabel, string? requestedYearLabel, int? suggestedYearFrom, int? suggestedYearTo, string language)
    {
        if (suggestedYearFrom is null || suggestedYearTo is null)
        {
            return language switch
            {
                "en" => $"We don't have a {vehicleLabel} in our database.",
                "fr" => $"Nous n'avons pas de {vehicleLabel} dans notre base de données.",
                "pt" => $"Não temos um {vehicleLabel} na nossa base de dados.",
                "es" => $"No tenemos un {vehicleLabel} en nuestra base de datos.",
                _ => $"Non abbiamo un {vehicleLabel} a catalogo.",
            };
        }

        // anno_fine_macchina uses 9999 to mean "still in production, no end
        // year yet" - stating that literally ("until 9999") would read as
        // nonsense to a mechanic, so it's phrased as "to today" instead.
        var stillInProduction = suggestedYearTo == 9999;
        var yearRangeText = language switch
        {
            "en" => stillInProduction ? $"from {suggestedYearFrom} to today" : $"from {suggestedYearFrom} to {suggestedYearTo}",
            "fr" => stillInProduction ? $"de {suggestedYearFrom} à aujourd'hui" : $"de {suggestedYearFrom} à {suggestedYearTo}",
            "pt" => stillInProduction ? $"de {suggestedYearFrom} até hoje" : $"de {suggestedYearFrom} a {suggestedYearTo}",
            "es" => stillInProduction ? $"de {suggestedYearFrom} a hoy" : $"de {suggestedYearFrom} a {suggestedYearTo}",
            _ => stillInProduction ? $"dal {suggestedYearFrom} a oggi" : $"dal {suggestedYearFrom} al {suggestedYearTo}",
        };

        return language switch
        {
            "en" => $"We don't have a {vehicleLabel} for {requestedYearLabel}, but we do have it {yearRangeText}.",
            "fr" => $"Nous n'avons pas de {vehicleLabel} pour {requestedYearLabel}, mais nous l'avons {yearRangeText}.",
            "pt" => $"Não temos um {vehicleLabel} para {requestedYearLabel}, mas temos {yearRangeText}.",
            "es" => $"No tenemos un {vehicleLabel} para {requestedYearLabel}, pero lo tenemos {yearRangeText}.",
            _ => $"Non abbiamo un {vehicleLabel} per il {requestedYearLabel}, ma è disponibile {yearRangeText}.",
        };
    }

    // marca/modello come from Gemini's own FindCar call args (the
    // mechanic's words, e.g. "FIAT"/"Panda") - falls back to a generic
    // per-language word only if Gemini called FindCar with neither, which
    // shouldn't happen in practice but isn't a crash-worthy condition.
    private static string BuildVehicleLabel(string? marca, string? modello, string language)
    {
        var label = string.Join(" ", new[] { marca, modello }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (label.Length > 0) return label;
        return language switch
        {
            "en" => "vehicle",
            "fr" => "véhicule",
            "pt" => "veículo",
            "es" => "vehículo",
            _ => "veicolo",
        };
    }

    // A single year ("2020") when only one bound was given or both match;
    // a range ("2018-2020") when they differ. Null only when neither bound
    // was given at all.
    private static string? BuildYearLabel(int? yearFrom, int? yearTo)
    {
        if (yearFrom is null && yearTo is null) return null;
        if (yearFrom is null) return yearTo.ToString();
        if (yearTo is null || yearTo == yearFrom) return yearFrom.ToString();
        return $"{yearFrom}-{yearTo}";
    }

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

    private static bool GetBool(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string BuildQuery(string baseUrl, params (string Key, string? Value)[] parameters)
    {
        var query = string.Join("&", parameters
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value!)}"));
        return query.Length == 0 ? baseUrl : $"{baseUrl}?{query}";
    }
}
