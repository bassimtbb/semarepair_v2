using Npgsql;
using SearchService.Models;

namespace SearchService.Services;

public class CoverageService
{
    private readonly string _connectionString;

    public CoverageService(IConfiguration configuration)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
    }

    public async Task<CoverageResponse> GetAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT
                (SELECT COUNT(DISTINCT from_id)
                 FROM graph_edges
                 WHERE from_type = 'car' AND relation = 'DOCUMENTED_IN') AS vehicles,
                (SELECT COUNT(DISTINCT (id_documento, language))
                 FROM documents
                 WHERE anomalia IS NOT NULL OR causa IS NOT NULL OR intervento IS NOT NULL) AS repair_documents,
                (SELECT COUNT(DISTINCT (id_documento, language))
                 FROM knowledge_chunks) AS technical_documents,
                (SELECT COUNT(*) FROM knowledge_chunks) AS technical_entries,
                (SELECT COALESCE(array_agg(DISTINCT language ORDER BY language), ARRAY[]::text[])
                 FROM (
                     SELECT language FROM documents
                     UNION
                     SELECT language FROM knowledge_chunks
                 ) archive_languages) AS languages
            """, connection);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new CoverageResponse();
        return new CoverageResponse
        {
            Vehicles = reader.GetInt64(0),
            RepairDocuments = reader.GetInt64(1),
            TechnicalDocuments = reader.GetInt64(2),
            TechnicalEntries = reader.GetInt64(3),
            Languages = reader.GetFieldValue<string[]>(4).ToList(),
        };
    }
}
