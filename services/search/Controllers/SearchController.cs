using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using SearchService.Models;
using SearchService.Services;

namespace SearchService.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    // Symptom search can hit exact or near-exact distance ties between
    // genuinely distinct documents (confirmed in real data: documents
    // 199309673/199309676 share byte-identical anomalia text, so any query
    // produces an exact tie). Below this gap, treat both as equally valid
    // matches instead of arbitrarily picking one - see decision A revision.
    private const double TieThreshold = 0.02;

    // Calibrated against real Gemini embeddings, not guessed: a true match
    // ("spia motore accesa scarse prestazioni" -> its own document) scored
    // 0.24, with a clear gap to the next candidate at 0.28. The single most
    // topically-relevant real brake document available ("freni che
    // stridono quando frenano" -> a real Freni/ABS document) scored 0.337,
    // while a confirmed car's genuinely unrelated documents (injection/fuel
    // faults) started at 0.398. Below this line a vector match is treated
    // as a real answer (existing tie-grouping behavior, unchanged); at or
    // above it, nothing in the candidate set actually answers the
    // question - see the real bug this fixed in progress.md.
    private const double MaxRelevantDistance = 0.35;

    // Calibrated separately from MaxRelevantDistance above, because the
    // corpus differs in kind: a chunk is a short labelled value
    // ("Centralina ABS · F04"), not a paragraph of symptom prose, so its
    // distances are not on the same scale.
    //
    // Measured over 34 real queries against the FI0396 data:
    //   22 legitimate questions  - all answered correctly, worst 0.288
    //     ("quale fusibile per le candelette" -> F02, 50 A)
    //   12 off-topic or absent   - best 0.305
    //     ("dove comprare i ricambi" -> a servicing chapter)
    // At 0.30: 22/22 kept, 0/12 wrongly accepted. At 0.31 the first false
    // positive appears; at 0.35, five do.
    //
    // The gap between the worst true answer and the best false one is only
    // 0.017, so this threshold is tight by nature rather than by choice -
    // re-measure it whenever the corpus changes materially, and do not nudge
    // it upward to rescue a single query.
    private const double MaxTechnicalDistance = 0.30;

    // How far behind the best match a chunk may sit and still be shown.
    //
    // Needed because the absolute threshold alone cannot separate "one right
    // answer" from "several": asking for the airbag wiring diagram returned
    // the airbag schematic at 0.215 AND three unrelated ones - ABS, ABS+ASR,
    // immobiliser - at 0.292-0.298, all under 0.30. They scored close because
    // every schematic's indexed text now begins with "Schema Elettrico", so
    // the words shared by the question outweighed the one word that
    // distinguishes them.
    //
    // Measured over 8 questions with a known number of right answers,
    // comparing windows of 0.03 to 0.08: 0.03 tracks the expected counts best
    // by a clear margin, takes the airbag query from 6 results to 1, and
    // leaves no question unanswered. Wider windows let the neighbours back in.
    private const double TechnicalRelativeWindow = 0.03;

    // A fuse question has one right answer; a "show me the diagram" question
    // legitimately returns a whole legend. The default is small enough to
    // stay readable and the caller may raise it.
    private const int TechnicalResultLimit = 6;
    private const int TechnicalMaxLimit = 30;

    // How many raw rows to pull per requested result before collapsing
    // legends. One diagram contributes up to 20 legend rows, so a factor
    // this size keeps a second document reachable behind the first.
    private const int LegendOverFetch = 8;

    private readonly GraphSearchService _graphSearch;
    private readonly VectorSearchService _vectorSearch;
    private readonly SymptomSearchService _symptomSearch;
    private readonly ValidationService _validation;
    private readonly DocumentContentService _documentContent;
    private readonly TechnicalSearchService _technicalSearch;

    public SearchController(
        GraphSearchService graphSearch,
        VectorSearchService vectorSearch,
        SymptomSearchService symptomSearch,
        ValidationService validation,
        DocumentContentService documentContent,
        TechnicalSearchService technicalSearch)
    {
        _graphSearch = graphSearch;
        _vectorSearch = vectorSearch;
        _symptomSearch = symptomSearch;
        _validation = validation;
        _documentContent = documentContent;
        _technicalSearch = technicalSearch;
    }

    // GET /api/search/fault-code?code=P2279&codiceMotore=XUJN&marca=FORD&lang=it
    // Italian query params (codiceMotore/marca) - a deliberate deviation
    // from docs/SemaRepair_Architecture.md section 6.4 (English params),
    // matching Vehicle Service's completed convention. See progress.md.
    [HttpGet("fault-code")]
    public async Task<SearchResponse> FaultCode(
        [FromQuery] string code, [FromQuery] string? codiceMotore, [FromQuery] string? marca, [FromQuery] string lang = "it")
    {
        if (codiceMotore is null)
        {
            var carIds = await _graphSearch.GetCarIdsForFaultAsync(code, lang);
            if (carIds.Count == 0) return NotFoundResponse();
            return BuildCarSelectionResponse(await _graphSearch.GetCarSummariesAsync(carIds));
        }

        var confirmedCarIds = await _graphSearch.ResolveCarIdsAsync(codiceMotore, marca);
        if (confirmedCarIds.Count == 0) return NotFoundResponse();

        var docs = await _graphSearch.GetDocumentsForCarsAndFaultAsync(confirmedCarIds, code, lang);
        if (docs.Count > 0)
            return await BuildDocumentResponseAsync(docs, lang, code);

        // Rule 8: determine the fault's System from ANY car's matching documents.
        var systems = await _graphSearch.GetSystemsForFaultAsync(code, lang);
        var fallback = await TryFallbackAsync(confirmedCarIds, codiceMotore, lang, code, systems,
            sharedCarIds => _graphSearch.GetDocumentsForCarsAndFaultAsync(sharedCarIds, code, lang));
        return fallback ?? NotFoundResponse();
    }

    // GET /api/search/symptom?q=ventola+radiatore&codiceMotore=F1AE0481C&marca=FIAT&lang=it
    [HttpGet("symptom")]
    public async Task<SearchResponse> Symptom(
        [FromQuery] string q, [FromQuery] string? codiceMotore, [FromQuery] string? marca, [FromQuery] string lang = "it")
    {
        var validation = _validation.ValidateSymptom(q);

        if (validation.Type == ValidationResultType.TooVague)
            return new SearchResponse { ResultType = "vague", Count = 0, ValidationMessage = validation.Reason };

        if (validation.Type == ValidationResultType.RedirectToFaultCode)
        {
            var redirected = await FaultCode(validation.FaultCode!, codiceMotore, marca, lang);
            redirected.RedirectedTo = validation.FaultCode;
            return redirected;
        }

        return codiceMotore is null
            ? await SymptomWithoutCarAsync(q, lang)
            : await SymptomWithCarAsync(q, codiceMotore, marca, lang);
    }

    // GET /api/search/system?name=Iniezione&codiceMotore=F1AE0481C&marca=FIAT&lang=it
    [HttpGet("system")]
    public async Task<SearchResponse> System(
        [FromQuery] string name, [FromQuery] string? codiceMotore, [FromQuery] string? marca, [FromQuery] string lang = "it")
    {
        if (codiceMotore is null)
        {
            var carIds = await _graphSearch.GetCarIdsForKeywordAsync(name, lang);
            if (carIds.Count == 0) return NotFoundResponse();
            return BuildCarSelectionResponse(await _graphSearch.GetCarSummariesAsync(carIds));
        }

        var confirmedCarIds = await _graphSearch.ResolveCarIdsAsync(codiceMotore, marca);
        if (confirmedCarIds.Count == 0) return NotFoundResponse();

        var docs = await _graphSearch.GetDocumentsForCarsAndKeywordAsync(confirmedCarIds, name, lang);
        if (docs.Count > 0)
            return await BuildDocumentResponseAsync(docs, lang, name);

        var fallback = await TryFallbackAsync(confirmedCarIds, codiceMotore, lang, name, [name],
            sharedCarIds => _graphSearch.GetDocumentsForCarsAndKeywordAsync(sharedCarIds, name, lang));
        return fallback ?? NotFoundResponse();
    }

    // GET /api/search/technical?q=quale+fusibile+per+ABS&codiceMotore=8140.43S&marca=FIAT&lang=it
    //
    // The extension's endpoint (docs/Architecture_Extension_v2.md). Answers
    // questions about the vehicle rather than about a fault: fuse ratings,
    // torque figures, bulb types, component locations, wiring diagrams,
    // service procedures.
    //
    // codiceMotore is REQUIRED, unlike the document endpoints which fall
    // back to a car-selection list when it is absent. A technical value is
    // meaningless without a vehicle, and there is no useful "which car did
    // you mean" answer to "what torque" - so an unconfirmed request is a
    // not_found, and the caller asks for the vehicle first.
    [HttpGet("technical")]
    public async Task<TechnicalResponse> Technical(
        [FromQuery] string q, [FromQuery] string? codiceMotore,
        [FromQuery] string? marca, [FromQuery] string lang = "it",
        [FromQuery] int limit = TechnicalResultLimit)
    {
        if (string.IsNullOrWhiteSpace(q) || string.IsNullOrWhiteSpace(codiceMotore))
            return new TechnicalResponse();

        var capped = Math.Clamp(limit, 1, TechnicalMaxLimit);

        // Over-fetch, then collapse, then cut to the caller's limit -
        // collapsing after a LIMIT would silently drop documents whose only
        // surviving rows fell outside the window.
        var chunks = await _technicalSearch.SearchAsync(
            q, codiceMotore, marca, lang, capped * LegendOverFetch);

        var withinThreshold = chunks.Where(c => c.Distance <= MaxTechnicalDistance).ToList();
        if (withinThreshold.Count == 0) return new TechnicalResponse();

        // Relative window, on top of the absolute threshold. The absolute one
        // answers "is anything here relevant at all"; this one answers "how
        // much of it belongs to the question that was asked", which a fixed
        // cutoff cannot: a question with one right answer and a question with
        // five both sit under 0.30.
        var best = withinThreshold.Min(c => c.Distance);
        var relevant = PreferAnswers(
                CollapseLegends(
                    withinThreshold
                        .Where(c => c.Distance <= best + TechnicalRelativeWindow)
                        .Where(IsUsableAnswer)))
            .Take(capped)
            .ToList();
        if (relevant.Count == 0) return new TechnicalResponse();

        return new TechnicalResponse
        {
            ResultType = "technical",
            Count = relevant.Count,
            Chunks = relevant,
        };
    }

    // Matches a legend entry whose label is nothing but an electrical rating:
    // "Fusibile 7,5A", "7.5A fuse", "Fusibile 50A". Deliberately keyed off
    // the rating rather than the noun, so it holds in every language.
    // Qualified via the using above rather than inline: this controller has
    // an action called System(), which shadows the namespace of the same name.
    private static readonly Regex BareRatingLabel =
        new(@"^\s*\D*\d+(?:[.,]\d+)?\s*A\D*\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A legend row that names a part by its rating identifies nothing: every
    // schematic has a 7.5 A fuse somewhere, so "Fusibile 7,5A" - which occurs
    // 16 times across 6 drawings - can never be the answer to a question.
    //
    // Found because "mostrami lo schema elettrico del fusibile F17" rendered
    // an entire ABS diagram in the conversation, pointing at its F03. On
    // screen that is far worse than a stray line of text: a large, confident
    // picture of the wrong thing.
    //
    // Only legends are filtered. The same rating on a `fact` IS the answer -
    // "F17 · Centralina Iniezione · 5 (A)" is exactly what was asked for -
    // and there it lives in `value`, not in the label.
    //
    // Checked against the data: these are the only legend labels containing a
    // digit at all, in either language the schematics exist in, so the rule
    // has no false positives here.
    private static bool IsUsableAnswer(TechnicalChunk c) =>
        c.Kind != "legend"
        || string.IsNullOrWhiteSpace(c.Label)
        || !BareRatingLabel.IsMatch(c.Label);

    // A wiring diagram's answer is the diagram, not one row of its legend.
    // Without this, "schema elettrico airbag" returns eight near-identical
    // rows from the same drawing - three of them literally "Fusibile 7,5A" -
    // and buries every other kind of result behind them. Measured on real
    // data: the top five hits for that query were all the same document.
    //
    // facts and sections are left alone: each one is a distinct answer
    // ("F04 · 50 A" and "F42 · 7,5 A" are two different fuses for the ABS,
    // and both are correct).
    //
    // Ordering is preserved because the input is already sorted by distance,
    // so the first legend seen for a document is its closest one.
    private static IEnumerable<TechnicalChunk> CollapseLegends(IEnumerable<TechnicalChunk> chunks)
    {
        var seenLegendDocs = new HashSet<string>();
        var seenLegendLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in chunks)
        {
            if (c.Kind == "legend")
            {
                if (!seenLegendDocs.Add(c.IdDocumento)) continue;
                // Also collapse by component name, not only by document.
                //
                // Some legend entries name a part by its rating rather than
                // its function - "Fusibile 7,5A" appears 16 times across 6
                // schematics - so they distinguish nothing and repeat as many
                // times as there are drawings. Asking for "lo schema elettrico
                // del fusibile F17" returned the correct F17 rating followed
                // by five identical "Fusibile 7,5A" rows from five unrelated
                // diagrams, which is noise dressed as five answers.
                //
                // When the name is specific ("Centralina Airbag") the closest
                // diagram is the right one and the rest add nothing either, so
                // the rule holds in both directions.
                if (c.Label is { Length: > 0 } label && !seenLegendLabels.Add(label)) continue;
            }
            yield return c;
        }
    }

    // Within a band of near-equal distances, put the chunks that actually
    // carry an answer first. A `fact` states a value ("ABS control unit ·
    // F42 · 7,5 A"); a `legend` only names a mark on a drawing, and several
    // diagrams carry near-content-free rows like "Fusibile 7,5A" that sit
    // very close to any fuse question.
    //
    // Found in English, where four such rows from four different schemas
    // scored 0.268-0.277 and pushed the real answer (0.271) to third place.
    // The Italian phrasing of the same question ranked the fact first, so
    // the ordering was language-dependent - not acceptable for a product
    // that answers in five.
    //
    // Banding rather than a flat kind priority: a genuinely closer legend
    // still wins. Same idea as TieThreshold for documents above, and the
    // same width, so the two search paths treat near-ties alike.
    private static IEnumerable<TechnicalChunk> PreferAnswers(IEnumerable<TechnicalChunk> chunks) =>
        chunks
            .Select((c, index) => (Chunk: c, Index: index))
            .OrderBy(x => (int)Math.Floor(x.Chunk.Distance / TieThreshold))
            .ThenBy(x => KindRank(x.Chunk.Kind))
            .ThenBy(x => x.Index)      // keeps the incoming distance order inside a band
            .Select(x => x.Chunk);

    // Only `fact` is promoted, and everything else keeps its distance order.
    // An earlier version also ranked `section` above `legend`, which looked
    // reasonable and was wrong: "schema elettrico ABS" then put a systems
    // list (0.258) above the ABS wiring diagram (0.257), demoting a closer
    // and more apt result. Kind cannot encode query intent - a section
    // answers "how do I reset the service indicator", a legend answers
    // "show me the diagram" - so the ordering only asserts the one thing
    // that holds regardless of the question: a stated value beats a pointer
    // to a drawing when the two are equally close.
    private static int KindRank(string kind) => kind == "fact" ? 0 : 1;

    // --- Search Type 3: symptom + confirmed car ---
    private async Task<SearchResponse> SymptomWithCarAsync(string symptom, string codiceMotore, string? marca, string lang)
    {
        var carIds = await _graphSearch.ResolveCarIdsAsync(codiceMotore, marca);
        if (carIds.Count == 0) return NotFoundResponse();

        var candidateDocs = await _graphSearch.GetDocumentsForCarsAsync(carIds);

        if (candidateDocs.Count == 0)
        {
            // No documents for this car at all - the only way to know if Rule
            // 8 fallback applies is to detect a System/Device in the symptom
            // text itself (there's no fault code/system param to look one up from).
            var matchedSystem = await _graphSearch.MatchSystemOrDeviceAsync(symptom, lang);
            if (matchedSystem is null || !SystemCategoryLookup.AllowsEngineFallback(matchedSystem))
                return NotFoundResponse();

            var sharedCarIds = await GetSharedEngineCarIdsAsync(carIds);
            if (sharedCarIds.Count == 0) return NotFoundResponse();

            var sharedDocs = await _graphSearch.GetDocumentsForCarsAsync(sharedCarIds);
            if (sharedDocs.Count == 0) return NotFoundResponse();

            var ranked = await _vectorSearch.RankWithinSetAsync(symptom, sharedDocs, lang);
            // Nothing close enough even among shared-engine vehicles - a
            // real not-found, not a doubly-qualified "low confidence AND
            // shared engine" guess (see MaxRelevantDistance).
            if (ranked.Count == 0 || ranked[0].Distance > MaxRelevantDistance) return NotFoundResponse();

            var tiedIds = GetTiedDocumentIds(ranked);
            var response = await BuildVectorMatchResponseAsync(tiedIds, lang);
            if (response.Documents.Count > 0)
            {
                var sharedVehicles = await BuildSharedEngineVehiclesAsync(tiedIds, sharedCarIds);
                foreach (var doc in response.Documents)
                {
                    doc.FoundViaSharedEngine = true;
                    doc.SharedEngineCodiceMotore = codiceMotore;
                    doc.SharedEngineVehicles = sharedVehicles;
                }
            }
            return response;
        }

        // Search Type 3 returns its closest vector match(es) - see decision A
        // and its revision above (TieThreshold): unlike fault-code/system,
        // there's no discrete "count" of exact matches to apply Rule 10's
        // 2-4/5+ buckets to, but distance ties between distinct documents do
        // happen in real data and shouldn't collapse to one arbitrary pick.
        var rankedCandidates = await _vectorSearch.RankWithinSetAsync(symptom, candidateDocs, lang);
        if (rankedCandidates.Count == 0) return NotFoundResponse();

        // Real bug found via a real query ("freni che stridono quando
        // frenano" against a car whose only documents are injection/fuel
        // faults): with no floor on relevance, the closest-but-unrelated
        // document was returned as if it answered the question. Below
        // MaxRelevantDistance, this is a genuine match (existing
        // tie-grouping behavior, unchanged). At or above it, surface only
        // the single nearest document, flagged as a low-confidence guess
        // rather than a confirmed answer - Chat Service's formatting step
        // is told not to present it as if it actually matched the symptom.
        if (rankedCandidates[0].Distance > MaxRelevantDistance)
        {
            var nearest = await BuildVectorMatchResponseAsync([rankedCandidates[0].IdDocumento], lang);
            foreach (var doc in nearest.Documents)
            {
                doc.LowConfidenceMatch = true;
                doc.LowConfidenceReason = $"{doc.Impianto} - {doc.Dispositivo}";
            }
            return nearest;
        }

        return await BuildVectorMatchResponseAsync(GetTiedDocumentIds(rankedCandidates), lang);
    }

    // --- Search Type 4: symptom, no car confirmed ---
    private async Task<SearchResponse> SymptomWithoutCarAsync(string symptom, string lang)
    {
        var matchedSystem = await _graphSearch.MatchSystemOrDeviceAsync(symptom, lang);

        List<(string IdDocumento, double Distance)> ranked;
        if (matchedSystem is not null)
        {
            var narrowedDocs = await _graphSearch.GetDocumentsForKeywordAsync(matchedSystem, lang);
            ranked = await _symptomSearch.FindBestMatchesAsync(
                symptom, lang, narrowedDocs.Count > 0 ? narrowedDocs : null);
        }
        else
        {
            ranked = await _symptomSearch.FindBestMatchesAsync(symptom, lang);
        }

        // No car confirmed yet, so this path can only ever surface a car
        // list, never the document itself - there's no field to attach a
        // "low confidence" disclaimer to a bare car list, so below
        // MaxRelevantDistance this is a real not-found rather than a
        // misleadingly confident car list (see the Type 3 fix above for
        // the same underlying gap).
        if (ranked.Count == 0 || ranked[0].Distance > MaxRelevantDistance) return NotFoundResponse();

        // Rule 1/2: never show the document itself before a car is confirmed -
        // so a distance tie between documents (see TieThreshold) just means
        // merging the car sets of every tied top document, since only the
        // car list is exposed here, not which document it came from.
        var cars = new List<CarSummary>();
        var seenCarIds = new HashSet<string>();
        foreach (var docId in GetTiedDocumentIds(ranked))
            foreach (var car in await _graphSearch.GetCarsForDocumentAsync(docId))
                if (seenCarIds.Add(car.IdMacchina))
                    cars.Add(car);

        return cars.Count == 0 ? NotFoundResponse() : BuildCarSelectionResponse(cars);
    }

    // Returns the IDs of every result within TieThreshold of the best
    // distance (ranked is pre-sorted ascending), so a genuine tie isn't
    // silently collapsed into one arbitrary pick.
    private static List<string> GetTiedDocumentIds(List<(string IdDocumento, double Distance)> ranked) =>
        ranked.TakeWhile(r => r.Distance - ranked[0].Distance <= TieThreshold)
            .Select(r => r.IdDocumento)
            .ToList();

    // --- Rule 8 fallback, shared by fault-code and system (both are plain
    // graph lookups - symptom's fallback is handled separately above since
    // it needs vector reranking, not just a graph re-query). ---
    private async Task<SearchResponse?> TryFallbackAsync(
        List<string> confirmedCarIds,
        string codiceMotore,
        string language,
        string queryText,
        IReadOnlyCollection<string> systemsToCheck,
        Func<List<string>, Task<List<string>>> searchWithCars)
    {
        if (!systemsToCheck.Any(SystemCategoryLookup.AllowsEngineFallback))
            return null;

        var sharedCarIds = await GetSharedEngineCarIdsAsync(confirmedCarIds);
        if (sharedCarIds.Count == 0) return null;

        var fallbackDocs = await searchWithCars(sharedCarIds);
        if (fallbackDocs.Count == 0) return null;

        var response = await BuildDocumentResponseAsync(fallbackDocs, language, queryText);
        var sharedVehicles = await BuildSharedEngineVehiclesAsync(fallbackDocs, sharedCarIds);
        foreach (var doc in response.Documents)
        {
            doc.FoundViaSharedEngine = true;
            doc.SharedEngineCodiceMotore = codiceMotore;
            doc.SharedEngineVehicles = sharedVehicles;
        }
        return response;
    }

    private async Task<List<string>> GetSharedEngineCarIdsAsync(IEnumerable<string> carIds)
    {
        var shared = new HashSet<string>();
        foreach (var carId in carIds)
            foreach (var sharedId in await _graphSearch.GetSharedEngineCarIdsAsync(carId))
                shared.Add(sharedId);
        return shared.ToList();
    }

    // Collects the brands/models behind a shared-engine result - only those
    // that actually produced a matching document, not every car that merely
    // shares the engine code. Returns the raw list; Chat Service turns it
    // into the mechanic-facing sentence in the conversation's own language
    // (it used to be formatted into Italian prose right here).
    private async Task<List<string>> BuildSharedEngineVehiclesAsync(
        List<string> matchedDocIds, List<string> sharedCarIds)
    {
        var relevantCarIds = new HashSet<string>();
        foreach (var docId in matchedDocIds)
        {
            var carsForDoc = await _graphSearch.GetCarsForDocumentAsync(docId);
            foreach (var car in carsForDoc)
                if (sharedCarIds.Contains(car.IdMacchina))
                    relevantCarIds.Add(car.IdMacchina);
        }

        var summaries = await _graphSearch.GetCarSummariesAsync(relevantCarIds);
        return summaries
            .Select(c => $"{c.Marca} {c.Modello}")
            .Distinct()
            .OrderBy(s => s)
            .ToList();
    }

    // --- Response builders ---

    // Rule 10 (Type 1/2 only): 1-4 docs → return all, ordered by reliability.
    // 5+ docs → vector-rerank the full set using queryText (the fault code or
    // system name the mechanic searched for), return the 3 closest, and signal
    // resultType="vague" so BuildFormatting generates a "here are the 3 most
    // relevant, add more detail to narrow it down" message. Count preserves the
    // total (N) so Gemini can tell the mechanic how many were found, not just 3.
    private async Task<SearchResponse> BuildDocumentResponseAsync(
        List<string> docIds, string language, string queryText)
    {
        var documents = (await _documentContent.GetDocumentResultsAsync(docIds, language))
            .OrderByDescending(d => d.Reliability)
            .ToList();

        if (documents.Count >= 5)
        {
            var totalCount = documents.Count;
            var ranked = await _vectorSearch.RankWithinSetAsync(queryText, docIds, language);
            List<DocumentResult> top3;
            if (ranked.Count > 0)
            {
                // Preserve distance order (closest first); docs without an
                // embedding in document_embeddings are simply absent from
                // ranked and fall back to whatever reliability-sorted docs
                // were already loaded.
                var top3Ids = ranked.Take(3).Select(r => r.IdDocumento).ToList();
                top3 = top3Ids
                    .Select(id => documents.FirstOrDefault(d => d.IdDocumento == id))
                    .Where(d => d is not null)
                    .Cast<DocumentResult>()
                    .ToList();
                if (top3.Count < 3)
                    top3.AddRange(documents.Where(d => !top3Ids.Contains(d.IdDocumento)).Take(3 - top3.Count));
            }
            else
            {
                top3 = documents.Take(3).ToList();
            }

            return new SearchResponse
            {
                ResultType = "vague",
                Count = totalCount,
                SelectionNeeded = true,
                Documents = top3,
                ValidationMessage = "Puoi essere più specifico?",
            };
        }

        return new SearchResponse
        {
            ResultType = documents.Count > 0 ? "document" : "not_found",
            Count = documents.Count,
            Documents = documents,
        };
    }

    // Search Type 3 (see decision A + TieThreshold revision): normally
    // exactly one document, but returns every document tied within
    // TieThreshold of the best vector match instead of arbitrarily picking
    // one. Order is preserved from the caller (closest first) - no Rule 10
    // reliability sort/truncation, since these aren't graph-exact matches.
    private async Task<SearchResponse> BuildVectorMatchResponseAsync(List<string> idDocumenti, string language)
    {
        var documents = await _documentContent.GetDocumentResultsAsync(idDocumenti, language);
        return new SearchResponse
        {
            ResultType = documents.Count > 0 ? "document" : "not_found",
            Count = documents.Count,
            Documents = documents,
        };
    }

    private static SearchResponse BuildCarSelectionResponse(List<CarSummary> cars) => new()
    {
        ResultType = "car_selection",
        Count = cars.Count,
        SelectionNeeded = true,
        Cars = cars,
    };

    private static SearchResponse NotFoundResponse() => new() { ResultType = "not_found", Count = 0 };
}
