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

    private readonly GraphSearchService _graphSearch;
    private readonly VectorSearchService _vectorSearch;
    private readonly SymptomSearchService _symptomSearch;
    private readonly ValidationService _validation;
    private readonly DocumentContentService _documentContent;

    public SearchController(
        GraphSearchService graphSearch,
        VectorSearchService vectorSearch,
        SymptomSearchService symptomSearch,
        ValidationService validation,
        DocumentContentService documentContent)
    {
        _graphSearch = graphSearch;
        _vectorSearch = vectorSearch;
        _symptomSearch = symptomSearch;
        _validation = validation;
        _documentContent = documentContent;
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
                var sharedInfo = await BuildSharedEngineInfoAsync(tiedIds, sharedCarIds, codiceMotore);
                foreach (var doc in response.Documents)
                {
                    doc.FoundViaSharedEngine = true;
                    doc.SharedEngineInfo = sharedInfo;
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
        var sharedInfo = await BuildSharedEngineInfoAsync(fallbackDocs, sharedCarIds, codiceMotore);
        foreach (var doc in response.Documents)
        {
            doc.FoundViaSharedEngine = true;
            doc.SharedEngineInfo = sharedInfo;
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

    // Builds the "Stesso motore (X): BRAND Model, BRAND Model" message -
    // only names brands/models that actually produced a matching document,
    // not every car that merely shares the engine code.
    private async Task<string> BuildSharedEngineInfoAsync(
        List<string> matchedDocIds, List<string> sharedCarIds, string codiceMotore)
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
        var brandsModels = summaries
            .Select(c => $"{c.Marca} {c.Modello}")
            .Distinct()
            .OrderBy(s => s);

        return $"Stesso motore ({codiceMotore}): {string.Join(", ", brandsModels)}";
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
