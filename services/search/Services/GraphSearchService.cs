using SearchService.Models;

namespace SearchService.Services;

// Graph traversal over graph_edges. See docs/SemaRepair_Architecture.md section 5.7/5.9.
public class GraphSearchService
{
    private readonly string _connectionString;

    public GraphSearchService(IConfiguration configuration)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
    }

    public Task<List<string>> GetDocumentsForCarAsync(string engineCode, string language) =>
        throw new NotImplementedException();

    public Task<List<string>> GetDocumentsForFaultCodeAsync(string faultCode, string language) =>
        throw new NotImplementedException();

    public Task<List<string>> GetCarsForDocumentAsync(string idDocumento) =>
        throw new NotImplementedException();

    public Task<List<string>> GetDocumentsForSystemOrDeviceAsync(string keyword, string language) =>
        throw new NotImplementedException();

    public Task<List<string>> GetSharedEngineCarsAsync(string engineCode) =>
        throw new NotImplementedException();
}
