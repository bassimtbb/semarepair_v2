using SearchService.Models;

namespace SearchService.Services;

// Vector similarity over symptom_embeddings (anomalia field only) - the
// no-car entry point. See docs/SemaRepair_Architecture.md section 5.6 Query 4.
public class SymptomSearchService
{
    private readonly string _connectionString;

    public SymptomSearchService(IConfiguration configuration)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
    }

    // Returns the best-matching idDocumento, or null if nothing close enough.
    public Task<string?> FindBestMatchAsync(string symptom, string language) =>
        throw new NotImplementedException();
}
