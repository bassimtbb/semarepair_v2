using System.Text.Json;
using System.Text.RegularExpressions;
using ChatService.Models;

namespace ChatService.Services;

// Gemini function calling orchestration: FindCar, SearchByFaultCode,
// SearchBySymptom, SearchBySystem. See docs/SemaRepair_Architecture.md
// section 2.4 (two-call pattern) and the full decision tree in section
// 5.10 (Rules 1-13). One HandleMessageAsync call = one /api/chat/stream
// request; multi-turn state (confirmed car, history) lives in Session,
// fetched/updated via SessionStore.
public partial class RepairOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly GeminiChatClient _gemini;
    private readonly SessionStore _sessions;
    private readonly ILogger<RepairOrchestrator> _logger;
    private readonly string _searchServiceUrl;
    private readonly string _vehicleServiceUrl;

    // Extension v2 (docs/Architecture_Extension_v2.md section 6). Read once at
    // construction: flipping it takes a container restart, which is the point -
    // the escape hatch has to be a deliberate act, not a per-request surprise.
    private readonly bool _technicalInfoEnabled;

    public RepairOrchestrator(HttpClient httpClient, GeminiChatClient gemini, SessionStore sessions, IConfiguration configuration, ILogger<RepairOrchestrator> logger)
    {
        _httpClient = httpClient;
        _gemini = gemini;
        _sessions = sessions;
        _logger = logger;
        _searchServiceUrl = configuration["SEARCH_SERVICE_URL"] ?? "";
        _vehicleServiceUrl = configuration["VEHICLE_SERVICE_URL"] ?? "";
        _technicalInfoEnabled = configuration.GetValue("TECHNICAL_INFO_ENABLED", false);
        _logger.LogInformation("[extension] TECHNICAL_INFO_ENABLED={Enabled}", _technicalInfoEnabled);
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
                tools: ToolDefinitions.For(_technicalInfoEnabled),
                systemInstruction: SystemPromptBuilder.BuildRouting(request.Language, _technicalInfoEnabled),
                operation: "routing",
                sessionId: request.SessionId,
                temperature: 0,
                disableThinking: true);
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
            // No tool ran, so no archive content exists for this turn - and
            // BuildRouting allows exactly one thing here: "do not call any
            // tool. Ask a short clarifying question directly". The prompt says
            // it; nothing used to enforce it, and the gap was not theoretical.
            //
            // Observed live, twice, on a confirmed Ducato. "Continua a
            // leggere." produced a complete reset procedure (trip-odometer
            // button, key to MAR, 10-15 seconds) that is nowhere in the
            // database - the real one is "premere il pulsante 1 per 5
            // secondi", with CFG1/CFG2/CFG3 intervals. A bare "go" produced
            // two entire fabricated repair sheets, announced as "Ho trovato 2
            // documenti di riparazione pertinenti", carrying invented fault
            // codes (P0190, P0335), invented pressures (200-250 bar) and an
            // invented ECU (EDC15C7).
            //
            // The mechanism is exact, and it is the price of a safety measure
            // taken elsewhere: BuildResultSummary deliberately keeps document
            // text out of History, so on a follow-up the model knows a
            // procedure was shown but holds not one word of it. Asked to
            // continue a text it cannot see, a language model writes one. The
            // measure that keeps the archive away from the model is what
            // creates the vacuum the model fills.
            //
            // So the shape is checked here rather than asked for in the
            // prompt, for the same reason BuildSharedEngineDisclosure is built
            // in C#: a guarantee the mechanic's safety rests on cannot be left
            // to the model's good behaviour. What survives is a short question;
            // anything else is replaced, and the mechanic is asked to say what
            // he needs. Losing a good answer to this test costs one rephrase.
            // Keeping a bad one costs a wrong repair.
            var routingText = routingTurn.Text;
            if (!IsClarifyingQuestion(routingText))
            {
                _logger.LogWarning(
                    "Routing returned no tool call and prose that is not a clarifying question "
                    + "({Length} chars) - replaced with the deterministic prompt. Text: {Text}",
                    routingText?.Length ?? 0, Truncate(routingText, 400));
                routingText = ClarifyRequestMessage(request.Language);
            }

            yield return new ChatResponse { Phase = "chat", Found = false, Message = routingText };
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
                rawResult = await CallServiceAsync(BuildSymptomSearchUrl(secondary, session, request.Language));
            }
            catch (Exception)
            {
                // Secondary search itself failed (service hiccup, not "no
                // match") - keep the original not_found result rather than
                // losing the turn; Rule 9b still fires correctly since
                // rawResult's resultType is still "not_found".
            }
        }

        var queryText = ExtractQueryText(routingTurn.FunctionCall);
        var resultSummary = BuildResultSummary(rawResult, lowConfidenceConfirmed, secondarySymptomTried, primarySymptomText, secondarySymptomText, queryText);
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
        // H2: the formatting call is the SECOND Gemini call of the turn and,
        // unlike the routing call above, it runs after the search already
        // succeeded - rawResult (with the verbatim causa/intervento cases) is
        // already in hand. If this call throws (e.g. Gemini 429/500), the
        // structured cases must still reach the frontend, so we catch here and
        // degrade to a plain fallback message rather than letting the exception
        // propagate out through the SSE await-foreach and break the stream
        // (losing the cases with it). The document text is never touched and is
        // NEVER re-generated through a fallback LLM call - only the prose
        // "message" is replaced, so document fidelity holds.
        string? message = null;

        // A technical answer that found something does not need a paid sentence.
        //
        // This call exists to write the line above the cards. For a technical
        // result the cards already carry the whole answer - the reference, the
        // rating, the enclosure - and what the model actually writes is the
        // template it was given, reordered: "Ecco le informazioni trovate
        // relativamente a: fusibile centralina iniezione" against
        // TechnicalFallbackMessage's "Ecco le informazioni tecniche trovate
        // 'fusibile centralina iniezione':". Same sentence, one of them free.
        //
        // Measured over the usage log, formatting is 39% of this service's
        // Gemini bill (88 calls, 1922 input tokens each) and a second of
        // latency on every question. It also remains one more place the model
        // can write something nobody checked - the same argument that put the
        // shared-engine disclosure and the not-found messages in C#.
        //
        // Gated on chunks being present, not merely on the tool having run:
        // with no chunks the technical branch in BuildChatResponse never
        // fires, the turn falls through to the generic not-found path, and
        // there the model's sentence earns its price by saying what was
        // missing. Only the "found it" case is templated.
        var technicalChunkCount =
            routingTurn.FunctionCall.Name == "SearchTechnicalInfo"
            && rawResult.ValueKind == JsonValueKind.Object
            && rawResult.TryGetProperty("chunks", out var formattingChunks)
            && formattingChunks.ValueKind == JsonValueKind.Array
                ? formattingChunks.GetArrayLength()
                : 0;

        if (technicalChunkCount > 0)
        {
            _logger.LogInformation(
                "Skipped the formatting call for a technical answer with {Count} chunk(s)",
                technicalChunkCount);
        }
        else try
        {
            var formattingTurn = await _gemini.GenerateAsync(
                session.History,
                systemInstruction: SystemPromptBuilder.BuildFormatting(request.Language),
                jsonMode: true,
                operation: "formatting",
                sessionId: request.SessionId);

            if (formattingTurn.Text is not null)
            {
                try
                {
                    message = JsonSerializer.Deserialize<FormattingResult>(formattingTurn.Text, JsonOptions)?.Message;
                }
                catch (JsonException)
                {
                    // Gemini returned a 2xx but not valid JSON for the message
                    // field - not fatal, the structured result (cases/carMatches)
                    // below is unaffected since it never depended on this call.
                }
            }
        }
        catch (Exception ex)
        {
            // Formatting call itself failed (non-2xx, network, no candidates).
            // Log for observability (like the boundary-tie log), keep the
            // structured cases, and substitute a neutral fallback line.
            _logger.LogWarning(ex,
                "Formatting call failed for session {SessionId}; returning structured result with fallback message",
                request.SessionId);
            message = FormattingFallbackMessage(request.Language);
        }

        // Rule 1 signal (M1, Half B): a car is confirmed this session if either
        // identity field is set. Both are normally set together by
        // ConfirmCarAsync/StoreConfirmedCar; the codiceMotore-only fallback path
        // sets ConfirmedCodiceMotore without ConfirmedCarId, and that IS a
        // legitimate confirmation (it drives the with-car search), so keying on
        // ConfirmedCarId alone would wrongly block it. Fires only when NOTHING
        // was confirmed.
        var carConfirmed = session.ConfirmedCarId is not null || session.ConfirmedCodiceMotore is not null;
        yield return BuildChatResponse(rawResult, message, routingTurn.FunctionCall, request.Language, lowConfidenceConfirmed, carConfirmed,
            session.ConfirmedCarLabel, queryText);
    }

    // phase/found/cases/carMatches are all derived directly from the real
    // Search/Vehicle Service response - never from Gemini's JSON output.
    // Instance (not static) so the Half B guard can log; carConfirmed is the
    // Rule 1 signal (M1).
    private ChatResponse BuildChatResponse(
        JsonElement rawResult, string? message, GeminiFunctionCall call, string language, bool lowConfidenceConfirmed, bool carConfirmed,
        string? confirmedCarLabel, string? queryText)
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

        // Extension v2: a technical question with no confirmed vehicle has one
        // right answer - ask which vehicle - and the generic path gives the
        // wrong one. /api/search/technical requires codiceMotore, so an
        // unconfirmed turn comes back empty and falls through to the
        // not_found branch, where Rule 9 tells the mechanic to describe the
        // problem better and asks which warning light is on. They did not
        // report a problem; they asked what a fuse is rated at. The advice is
        // not merely useless, it points at a dead end.
        //
        // A diagnostic search in the same position offers a vehicle list, so
        // this also removes an inconsistency the mechanic would feel before
        // they could name it.
        if (call.Name == "SearchTechnicalInfo" && !carConfirmed)
        {
            return new ChatResponse
            {
                Phase = "identification",
                Found = false,
                Message = IdentifyVehicleFirstMessage(language),
            };
        }

        // A technical result carries neither cars nor documents, so without
        // this branch it would fall through to the generic "nothing found"
        // return below and the chunks would be dropped.
        if (rawResult.ValueKind == JsonValueKind.Object &&
            rawResult.TryGetProperty("chunks", out var chunks) &&
            chunks.ValueKind == JsonValueKind.Array &&
            chunks.GetArrayLength() > 0)
        {
            // Same structural backstop as Half B below. Unreachable in
            // practice - /api/search/technical requires codiceMotore and
            // BuildSearchUrl only ever sends the confirmed session value, so
            // an unconfirmed turn already comes back empty. Kept because
            // "unreachable" is a property of today's call path, not a
            // guarantee, and a torque figure for the wrong engine is exactly
            // the kind of wrong answer Rule 1 exists to prevent.
            if (!carConfirmed)
            {
                _logger.LogWarning(
                    "Rule 1 guard (M1): suppressed technical chunks for tool {Tool} with no confirmed car",
                    call.Name);
                return new ChatResponse
                {
                    Phase = "identification",
                    Found = false,
                    Message = IdentifyVehicleFirstMessage(language),
                };
            }

            // A floor, not a replacement: when the model writes its own line we
            // keep it, because a varied sentence reads better than a template.
            // But it omitted one on two questions out of five in testing, and
            // a bare list of values with no sentence reads as if the question
            // had been ignored. Same reasoning as the Rule 8 disclosure -
            // where the cost of the model staying silent is real, the floor is
            // built here rather than asked for.
            return new ChatResponse
            {
                Phase = "chat",
                Found = true,
                Message = string.IsNullOrWhiteSpace(message)
                    ? TechnicalFallbackMessage(queryText, language)
                    : message,
                TechnicalChunks = chunks.EnumerateArray().Select(ParseTechnicalChunk).ToList(),
            };
        }

        if (rawResult.ValueKind == JsonValueKind.Object &&
            rawResult.TryGetProperty("documents", out var docs) &&
            docs.ValueKind == JsonValueKind.Array &&
            docs.GetArrayLength() > 0)
        {
            // Half B (M1 / Rule 1): structural backstop - a document result must
            // never be emitted without a confirmed car, whatever Gemini put in
            // the tool args. Half A stops the known trigger (an unconfirmed
            // engine code seeding the with-car search); this stops EVERY other
            // path too, so Rule 1 no longer depends on the model's discretion.
            // After Half A this is unreachable in the known flows (documents
            // only come back when session.ConfirmedCodiceMotore was passed) -
            // if it ever fires, a with-car search ran unconfirmed and we refuse
            // to show the document, asking the mechanic to identify the vehicle.
            if (!carConfirmed)
            {
                _logger.LogWarning(
                    "Rule 1 guard (M1): suppressed document emission for tool {Tool} with no confirmed car",
                    call.Name);
                return new ChatResponse
                {
                    Phase = "identification",
                    Found = false,
                    Message = IdentifyVehicleFirstMessage(language),
                };
            }

            var first = docs.EnumerateArray().First();
            var isLowConfidence = first.ValueKind == JsonValueKind.Object &&
                first.TryGetProperty("lowConfidenceMatch", out var lc) && lc.ValueKind == JsonValueKind.True;

            // Rule 8 transparency, composed here rather than asked of Gemini:
            // showing another brand's procedure without saying where it came
            // from is the one failure this rule exists to prevent, and a
            // model that silently drops the disclosure once is a mechanic
            // fitting a FIAT Ducato part to a Citroen. Prepended rather than
            // substituted so Rule 8c/10's own framing still gets through.
            message = PrependSharedEngineDisclosure(first, message, confirmedCarLabel, queryText, language);

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

    // What the mechanic actually searched for, as it went into the tool call -
    // the DTC, the cleaned symptom text, or the system name. Needed by the
    // formatting call's car-selection rule, which has to name the search term
    // back to the mechanic ("Il codice P0380 compare in N veicoli"); the
    // metadata blob otherwise carries only counts and flags.
    private static string? ExtractQueryText(GeminiFunctionCall call) =>
        GetString(call.Args, "faultCode")
        ?? GetString(call.Args, "symptom")
        ?? GetString(call.Args, "systemName")
        ?? GetString(call.Args, "query");   // SearchTechnicalInfo

    private static object BuildResultSummary(
        JsonElement rawResult, bool lowConfidenceConfirmed,
        bool secondarySymptomTried, string? primarySymptomText, string? secondarySymptomText,
        string? queryText)
    {
        bool foundViaSharedEngine = false;
        bool lowConfidenceMatch = false;
        string? lowConfidenceReason = null;
        if (rawResult.ValueKind == JsonValueKind.Object &&
            rawResult.TryGetProperty("documents", out var docs) &&
            docs.ValueKind == JsonValueKind.Array)
        {
            var first = docs.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object)
            {
                // sharedEngineInfo is deliberately NOT forwarded to Gemini any
                // more: the Rule 8 disclosure is built deterministically in
                // BuildSharedEngineDisclosure and prepended in
                // BuildChatResponse, so the model has nothing to restate. The
                // flag stays because Rule 10 still needs to know.
                foundViaSharedEngine = first.TryGetProperty("foundViaSharedEngine", out var f) &&
                    f.ValueKind == JsonValueKind.True;
                lowConfidenceMatch = first.TryGetProperty("lowConfidenceMatch", out var lc) &&
                    lc.ValueKind == JsonValueKind.True;
                lowConfidenceReason = GetString(first, "lowConfidenceReason");
            }
        }

        // Extension v2: which kinds of answer came back, so the formatting
        // call can frame "here is the value" differently from "here is the
        // diagram". The content itself is never sent - the frontend renders
        // it from the database, same rule as document bodies.
        string? technicalKinds = null;
        if (rawResult.ValueKind == JsonValueKind.Object &&
            rawResult.TryGetProperty("chunks", out var techChunks) &&
            techChunks.ValueKind == JsonValueKind.Array &&
            techChunks.GetArrayLength() > 0)
        {
            technicalKinds = string.Join(",", techChunks.EnumerateArray()
                .Select(c => GetString(c, "kind"))
                .Where(k => k is not null)
                .Distinct());
        }

        return new
        {
            resultType = GetString(rawResult, "resultType"),
            count = GetInt(rawResult, "count"),
            queryText,
            technicalKinds,
            foundViaSharedEngine,
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

    // Same per-language switch shape as FormattingFallbackMessage and
    // BuildVehicleNotFoundMessage. Names what was looked up and stops there:
    // the values themselves are rendered from the database below, and
    // restating them here would mean inventing them.
    // True only for the one thing BuildRouting permits when no tool runs: a
    // short question. Every test below is a property of the ANSWER's shape or
    // of what it CLAIMS, never of its subject - deciding whether prose is
    // "technical" is exactly the judgement that cannot be made reliably, and
    // does not need to be.
    //
    // This is a shape test, not a truth test. It catches the observed failure
    // mode and raises the bar a fabrication has to clear; it is not a proof
    // that what survives is true. What makes that acceptable is the corollary:
    // anything the mechanic is SHOWN as archive content - documents, chunks,
    // values - is spliced from the tool result in BuildChatResponse and never
    // passes through the model at all. This path carries framing text only.
    private static bool IsClarifyingQuestion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        // A question asks. An answer asserts. This is the test that carries
        // the rule. The second mark is the full-width question mark, which
        // Gemini emits in CJK contexts - cheap to accept, and its absence
        // would be a silent hole.
        if (!text.Contains('?') && !text.Contains('\uff1f')) return false;

        // Not a heuristic but an impossibility: no tool ran, so nothing was
        // retrieved, so any claim of having found something is false by
        // construction. The worst observed fabrication opened with exactly
        // this - "Ho trovato 2 documenti di riparazione pertinenti" - before
        // inventing both of them, with fault codes and rail pressures.
        if (ClaimsAFinding().IsMatch(text)) return false;

        // Headings, horizontal rules and numbered steps are how findings get
        // presented, and there are no findings here. Bullets are NOT rejected:
        // a legitimate clarifying question lists what would make the question
        // searchable, and rejecting those replaced a perfectly good greeting
        // in testing.
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#')) return false;
            if (trimmed.StartsWith("---") || trimmed.StartsWith("***")) return false;
            if (NumberedStep().IsMatch(trimmed)) return false;
        }

        // The weakest of the tests and deliberately generous: a backstop for a
        // fabrication written as flowing prose that happens to end in a
        // question mark. Calibrated against a real greeting that runs to 380
        // characters over six lines, so the cap has to clear that comfortably.
        return text.Length <= 700 && text.Split('\n').Length <= 12;
    }

    // "I found", "here are the documents", in the five languages, at the start
    // of a line or a sentence. Kept narrow on purpose: it must fire on a claim
    // of retrieval and stay silent on "what did you find?" or "I can look for
    // that", which are ordinary things to say in a clarifying question.
    [GeneratedRegex(
        @"(?im)^\W*(ho\s+trovato|abbiamo\s+trovato|ecco\s+(i|le|la|il)\s+"
        + @"(document|schede|scheda|procedur)|i\s+found|i\s+have\s+found|here\s+are\s+the\s+"
        + @"(document|repair|procedur)|j'?ai\s+trouv|voici\s+les\s+(document|fiches|proc)|"
        + @"he\s+encontrado|encontr(e|é)\s+|aqui\s+est(a|á)n\s+los\s+document|"
        + @"encontrei\s+|aqui\s+est(a|ã)o\s+os\s+document)")]
    private static partial Regex ClaimsAFinding();

    [GeneratedRegex(@"^\d{1,2}[.)]\s")]
    private static partial Regex NumberedStep();

    private static string? Truncate(string? text, int max) =>
        text is null || text.Length <= max ? text : text[..max] + "...";

    // What the mechanic is asked instead. Deterministic, like
    // BuildVehicleNotFoundMessage: this fires precisely when the model was
    // about to answer from nothing, so it cannot be written by the model.
    //
    // It names what would make the question searchable, which is the same list
    // BuildRouting gives the model for its own clarifying questions - so the
    // replacement asks for exactly what the real one would have asked for.
    private static string ClarifyRequestMessage(string language) => language switch
    {
        "en" => "I can only answer from the documentation in your archive, and I have nothing "
              + "to search on yet. Which system or component is affected, when does the problem "
              + "happen, or is there a warning light or a fault code (DTC)?",
        "fr" => "Je ne peux répondre qu'à partir de la documentation de votre archive, et je n'ai "
              + "pas encore de quoi chercher. Quel système ou composant est concerné, quand le "
              + "problème se produit-il, ou y a-t-il un voyant allumé ou un code défaut (DTC) ?",
        "pt" => "Só posso responder a partir da documentação do seu arquivo, e ainda não tenho "
              + "nada para pesquisar. Que sistema ou componente está em causa, quando ocorre o "
              + "problema, ou há alguma luz de aviso ou código de avaria (DTC)?",
        "es" => "Solo puedo responder a partir de la documentación de su archivo, y todavía no "
              + "tengo nada que buscar. ¿Qué sistema o componente está afectado, cuándo se "
              + "produce el problema, o hay algún testigo encendido o código de avería (DTC)?",
        _ => "Posso rispondere solo con la documentazione del vostro archivio, e non ho ancora "
           + "nulla su cui cercare. Quale sistema o componente è coinvolto, quando si presenta "
           + "il problema, oppure c'è una spia accesa o un codice guasto (DTC)?",
    };

    private static string TechnicalFallbackMessage(string? queryText, string language)
    {
        var subject = string.IsNullOrWhiteSpace(queryText) ? null : $" \"{queryText}\"";
        return language switch
        {
            "en" => $"Here is the technical information found{subject}:",
            "fr" => $"Voici les informations techniques trouvées{subject} :",
            "pt" => $"Aqui estão as informações técnicas encontradas{subject}:",
            "es" => $"Esta es la información técnica encontrada{subject}:",
            _ => $"Ecco le informazioni tecniche trovate{subject}:",
        };
    }

    // Spliced field by field from Search Service's JSON, like ParseCaseSummary
    // and ParseCarOption - Gemini never touches this content.
    private static TechnicalChunk ParseTechnicalChunk(JsonElement c) => new()
    {
        IdDocumento   = GetString(c, "idDocumento") ?? "",
        Language      = GetString(c, "language") ?? "",
        Kind          = GetString(c, "kind") ?? "",
        Heading       = GetString(c, "heading"),
        Label         = GetString(c, "label"),
        Value         = GetString(c, "value"),
        Unit          = GetString(c, "unit"),
        Reference     = GetString(c, "reference"),
        Body          = GetString(c, "body"),
        AssetId       = GetString(c, "assetId"),
        DocumentTitle = GetString(c, "documentTitle"),
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
            "SearchTechnicalInfo" => await CallServiceAsync(BuildSearchUrl("technical", "q", "query", call.Args, session, language)),
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

    // Engine code/brand come ONLY from confirmed session state, never from
    // Gemini's tool args (M1 / Rule 1, Half A). A codiceMotore passed to
    // Search Service selects the with-car document path; sourcing it from
    // args meant a bare engine code the mechanic typed in free text
    // ("...motore 8140.43S") seeded a confirmed-car document search with no
    // car ever confirmed - a Rule 1 leak, AND product-wrong since one engine
    // code is ambiguous (8140.43S maps to 14 cars across 4 brands). With the
    // args fallback removed, an unconfirmed engine code leaves codiceMotore
    // null, so Search Service takes its no-car path and returns a car
    // SELECTION (identification) instead - exactly how a bare model name is
    // treated. When a car IS confirmed the session value is used, unchanged.
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
        var codiceMotore = session.ConfirmedCodiceMotore;
        var marca = session.ConfirmedMarca;
        return BuildQuery($"{_searchServiceUrl}/api/search/{endpoint}",
            (queryParam, GetString(args, argName)),
            ("codiceMotore", codiceMotore),
            ("marca", marca),
            ("lang", language));
    }

    // Re-runs a symptom search with literal text rather than Gemini's own
    // call args - used only for the secondary-symptom retry above, where
    // the text to search ("secondarySymptom") is separate from the
    // original call's "symptom" argument. engineCode/brand come ONLY from
    // confirmed session state (M1 / Rule 1, Half A - same reasoning as
    // BuildSearchUrl: an unconfirmed engine code must not seed a with-car
    // document search); originalArgs is no longer read for them.
    private string BuildSymptomSearchUrl(string symptomText, Session session, string language)
    {
        var codiceMotore = session.ConfirmedCodiceMotore;
        var marca = session.ConfirmedMarca;
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

    // Rule 8 disclosure (docs/SemaRepair_Architecture.md section 5.6). Built
    // here, deterministically, instead of being left to the formatting call:
    // the whole point of the rule is that a mechanic is NEVER shown another
    // brand's procedure without being told, so it can't depend on the model
    // choosing to mention it. Returns `message` untouched when the document
    // wasn't found via the shared-engine fallback - a direct hit gets no
    // disclosure, which is what keeps this quiet on a normal search.
    //
    // Reads the structured fields Search Service now sends
    // (sharedEngineCodiceMotore + sharedEngineVehicles, per DocumentResult);
    // it used to receive a pre-built Italian sentence and hand it to Gemini
    // to paraphrase, which was both language-wrong and droppable.
    private static string? PrependSharedEngineDisclosure(
        JsonElement firstDoc, string? message, string? confirmedCarLabel, string? queryText, string language)
    {
        if (firstDoc.ValueKind != JsonValueKind.Object ||
            !firstDoc.TryGetProperty("foundViaSharedEngine", out var flag) ||
            flag.ValueKind != JsonValueKind.True)
        {
            return message;
        }

        var engineCode = GetString(firstDoc, "sharedEngineCodiceMotore");
        var vehicles = new List<string>();
        if (firstDoc.TryGetProperty("sharedEngineVehicles", out var v) && v.ValueKind == JsonValueKind.Array)
            vehicles.AddRange(v.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s))!);

        var disclosure = BuildSharedEngineDisclosure(confirmedCarLabel, queryText, engineCode, vehicles, language);
        return string.IsNullOrWhiteSpace(message) ? disclosure : $"{disclosure}\n\n{message}";
    }

    // Same shape as BuildVehicleNotFoundMessage above: per-language switch,
    // Italian as the default arm. Every variable part comes from real data -
    // the confirmed car's own label, what was searched, and the engine code
    // Search Service matched on - never a placeholder.
    private static string BuildSharedEngineDisclosure(
        string? confirmedCarLabel, string? queryText, string? engineCode, List<string> vehicles, string language)
    {
        var vehicleLabel = string.IsNullOrWhiteSpace(confirmedCarLabel)
            ? BuildVehicleLabel(null, null, language)
            : confirmedCarLabel;

        // "for your CITROEN Jumper with code P0380" vs. just "for your
        // CITROEN Jumper" - queryText is null only if the tool was called
        // with no searchable argument at all, which shouldn't happen but
        // mustn't produce "with code ".
        var forQuery = string.IsNullOrWhiteSpace(queryText) ? "" : language switch
        {
            "en" => $" with code {queryText}",
            "fr" => $" avec le code {queryText}",
            "pt" => $" com o código {queryText}",
            "es" => $" con el código {queryText}",
            _ => $" con il codice {queryText}",
        };

        var engineLabel = string.IsNullOrWhiteSpace(engineCode) ? "" : $" {engineCode}";

        // The vehicles are also rendered as document cards right below, so
        // naming them here is a summary, not the only place they appear -
        // and when the list is empty the sentence still stands on its own.
        var vehicleList = vehicles.Count == 0 ? "" : $" ({string.Join(", ", vehicles)})";

        return language switch
        {
            "en" => $"I found no documents for your {vehicleLabel}{forQuery}. I did find documents for other vehicles fitted with the same engine{engineLabel}{vehicleList}:",
            "fr" => $"Je n'ai trouvé aucun document pour votre {vehicleLabel}{forQuery}. J'ai en revanche trouvé des documents pour d'autres véhicules équipés du même moteur{engineLabel}{vehicleList} :",
            "pt" => $"Não encontrei documentos para o seu {vehicleLabel}{forQuery}. Encontrei, no entanto, documentos para outros veículos que montam o mesmo motor{engineLabel}{vehicleList}:",
            "es" => $"No he encontrado documentos para tu {vehicleLabel}{forQuery}. Sí he encontrado documentos para otros vehículos que montan el mismo motor{engineLabel}{vehicleList}:",
            _ => $"Non ho trovato documenti per il tuo {vehicleLabel}{forQuery}. Ho però trovato documenti per altri veicoli che montano lo stesso motore{engineLabel}{vehicleList}:",
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

    // Half B (M1) message: shown only if the Rule 1 guard fires (a document
    // result reached BuildChatResponse with no confirmed car). Fixed
    // per-language, never LLM-generated, mirroring the other per-language
    // switches here.
    private static string IdentifyVehicleFirstMessage(string language) => language switch
    {
        "en" => "Which vehicle is this for? I can show the repair documentation once the vehicle is confirmed.",
        "fr" => "Pour quel véhicule ? Je peux afficher la documentation une fois le véhicule confirmé.",
        "pt" => "Para qual veículo? Posso mostrar a documentação depois de confirmar o veículo.",
        "es" => "¿Para qué vehículo? Puedo mostrar la documentación una vez confirmado el vehículo.",
        _ => "Per quale veicolo? Posso mostrare la documentazione una volta confermato il veicolo.",
    };

    // H2 fallback prose when the formatting Gemini call fails but structured
    // results are already in hand. Deliberately NOT the service-unavailable
    // string (which would misread when results are actually being shown) and
    // NOT generated by any LLM - a fixed per-language "here are the results"
    // line, mirroring ServiceUnavailableResponse's own per-language switch.
    private static string FormattingFallbackMessage(string language) => language switch
    {
        "en" => "Here are the results I found.",
        "fr" => "Voici les résultats trouvés.",
        "pt" => "Aqui estão os resultados encontrados.",
        "es" => "Aquí están los resultados encontrados.",
        _ => "Ecco i risultati trovati.",
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
