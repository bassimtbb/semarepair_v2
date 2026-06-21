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

    // GET /api/search/fault-code?code=P2279&engine=XUJN&brand=FORD&lang=it
    [HttpGet("fault-code")]
    public async Task<SearchResponse> FaultCode(
        [FromQuery] string code, [FromQuery] string? engine, [FromQuery] string? brand, [FromQuery] string lang = "it")
    {
        if (engine is null)
        {
            var carIds = await _graphSearch.GetCarIdsForFaultAsync(code, lang);
            if (carIds.Count == 0) return NotFoundResponse();
            return BuildCarSelectionResponse(await _graphSearch.GetCarSummariesAsync(carIds));
        }

        var confirmedCarIds = await _graphSearch.ResolveCarIdsAsync(engine, brand);
        if (confirmedCarIds.Count == 0) return NotFoundResponse();

        var docs = await _graphSearch.GetDocumentsForCarsAndFaultAsync(confirmedCarIds, code, lang);
        if (docs.Count > 0)
            return await BuildDocumentResponseAsync(docs, lang);

        // Rule 8: determine the fault's System from ANY car's matching documents.
        var systems = await _graphSearch.GetSystemsForFaultAsync(code, lang);
        var fallback = await TryFallbackAsync(confirmedCarIds, engine, lang, systems,
            sharedCarIds => _graphSearch.GetDocumentsForCarsAndFaultAsync(sharedCarIds, code, lang));
        return fallback ?? NotFoundResponse();
    }

    // GET /api/search/symptom?q=ventola+radiatore&engine=F1AE0481C&brand=FIAT&lang=it
    [HttpGet("symptom")]
    public async Task<SearchResponse> Symptom(
        [FromQuery] string q, [FromQuery] string? engine, [FromQuery] string? brand, [FromQuery] string lang = "it")
    {
        var validation = _validation.ValidateSymptom(q);

        if (validation.Type == ValidationResultType.TooVague)
            return new SearchResponse { ResultType = "vague", Count = 0, ValidationMessage = validation.Reason };

        if (validation.Type == ValidationResultType.RedirectToFaultCode)
        {
            var redirected = await FaultCode(validation.FaultCode!, engine, brand, lang);
            redirected.RedirectedTo = validation.FaultCode;
            return redirected;
        }

        return engine is null
            ? await SymptomWithoutCarAsync(q, lang)
            : await SymptomWithCarAsync(q, engine, brand, lang);
    }

    // GET /api/search/system?name=Iniezione&engine=F1AE0481C&brand=FIAT&lang=it
    [HttpGet("system")]
    public async Task<SearchResponse> System(
        [FromQuery] string name, [FromQuery] string? engine, [FromQuery] string? brand, [FromQuery] string lang = "it")
    {
        if (engine is null)
        {
            var carIds = await _graphSearch.GetCarIdsForKeywordAsync(name, lang);
            if (carIds.Count == 0) return NotFoundResponse();
            return BuildCarSelectionResponse(await _graphSearch.GetCarSummariesAsync(carIds));
        }

        var confirmedCarIds = await _graphSearch.ResolveCarIdsAsync(engine, brand);
        if (confirmedCarIds.Count == 0) return NotFoundResponse();

        var docs = await _graphSearch.GetDocumentsForCarsAndKeywordAsync(confirmedCarIds, name, lang);
        if (docs.Count > 0)
            return await BuildDocumentResponseAsync(docs, lang);

        var fallback = await TryFallbackAsync(confirmedCarIds, engine, lang, [name],
            sharedCarIds => _graphSearch.GetDocumentsForCarsAndKeywordAsync(sharedCarIds, name, lang));
        return fallback ?? NotFoundResponse();
    }

    // --- Search Type 3: symptom + confirmed car ---
    private async Task<SearchResponse> SymptomWithCarAsync(string symptom, string engine, string? brand, string lang)
    {
        var carIds = await _graphSearch.ResolveCarIdsAsync(engine, brand);
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
            if (ranked.Count == 0) return NotFoundResponse();

            var tiedIds = GetTiedDocumentIds(ranked);
            var response = await BuildVectorMatchResponseAsync(tiedIds, lang);
            if (response.Documents.Count > 0)
            {
                var sharedInfo = await BuildSharedEngineInfoAsync(tiedIds, sharedCarIds, engine);
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
        return rankedCandidates.Count == 0
            ? NotFoundResponse()
            : await BuildVectorMatchResponseAsync(GetTiedDocumentIds(rankedCandidates), lang);
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

        if (ranked.Count == 0) return NotFoundResponse();

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
        string engineCode,
        string language,
        IReadOnlyCollection<string> systemsToCheck,
        Func<List<string>, Task<List<string>>> searchWithCars)
    {
        if (!systemsToCheck.Any(SystemCategoryLookup.AllowsEngineFallback))
            return null;

        var sharedCarIds = await GetSharedEngineCarIdsAsync(confirmedCarIds);
        if (sharedCarIds.Count == 0) return null;

        var fallbackDocs = await searchWithCars(sharedCarIds);
        if (fallbackDocs.Count == 0) return null;

        var response = await BuildDocumentResponseAsync(fallbackDocs, language);
        var sharedInfo = await BuildSharedEngineInfoAsync(fallbackDocs, sharedCarIds, engineCode);
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
        List<string> matchedDocIds, List<string> sharedCarIds, string engineCode)
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

        return $"Stesso motore ({engineCode}): {string.Join(", ", brandsModels)}";
    }

    // --- Response builders ---

    // Rule 10 (Type 1/2 only - see decision A): ordered by reliability,
    // truncated to 3 with a clarification hint once there are 5+ matches.
    private async Task<SearchResponse> BuildDocumentResponseAsync(List<string> docIds, string language)
    {
        var documents = (await _documentContent.GetDocumentResultsAsync(docIds, language))
            .OrderByDescending(d => d.Reliability)
            .ToList();

        string? validationMessage = null;
        if (documents.Count >= 5)
        {
            documents = documents.Take(3).ToList();
            validationMessage = "Puoi essere più specifico?";
        }

        return new SearchResponse
        {
            ResultType = documents.Count > 0 ? "document" : "not_found",
            Count = documents.Count,
            Documents = documents,
            ValidationMessage = validationMessage,
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
