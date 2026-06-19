using SearchService.Models;

namespace SearchService.Services;

// Vector similarity over document_embeddings. See docs/SemaRepair_Architecture.md section 6.8/8.3.
public class VectorSearchService
{
    private readonly string _connectionString;

    public VectorSearchService(IConfiguration configuration)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
    }

    // Reranks within a pre-filtered set of document IDs (Search Type 3).
    public Task<string?> RankWithinSetAsync(string symptom, IEnumerable<string> idDocumentoCandidates, string language) =>
        throw new NotImplementedException();
}
