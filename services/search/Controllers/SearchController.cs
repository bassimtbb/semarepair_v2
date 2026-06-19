using Microsoft.AspNetCore.Mvc;
using SearchService.Models;
using SearchService.Services;

namespace SearchService.Controllers;

[ApiController]
[Route("api/search")]
public class SearchController : ControllerBase
{
    private readonly GraphSearchService _graphSearch;
    private readonly VectorSearchService _vectorSearch;
    private readonly SymptomSearchService _symptomSearch;
    private readonly ValidationService _validation;

    public SearchController(
        GraphSearchService graphSearch,
        VectorSearchService vectorSearch,
        SymptomSearchService symptomSearch,
        ValidationService validation)
    {
        _graphSearch = graphSearch;
        _vectorSearch = vectorSearch;
        _symptomSearch = symptomSearch;
        _validation = validation;
    }

    // GET /api/search/fault-code?code=P2279&engine=XUJN&lang=it
    [HttpGet("fault-code")]
    public Task<SearchResponse> FaultCode([FromQuery] string code, [FromQuery] string? engine, [FromQuery] string lang = "it") =>
        throw new NotImplementedException();

    // GET /api/search/symptom?q=ventola+radiatore&engine=F1AE0481C&lang=it
    [HttpGet("symptom")]
    public Task<SearchResponse> Symptom([FromQuery] string q, [FromQuery] string? engine, [FromQuery] string lang = "it") =>
        throw new NotImplementedException();

    // GET /api/search/system?name=Iniezione&engine=F1AE0481C&lang=it
    [HttpGet("system")]
    public Task<SearchResponse> System([FromQuery] string name, [FromQuery] string? engine, [FromQuery] string lang = "it") =>
        throw new NotImplementedException();
}
