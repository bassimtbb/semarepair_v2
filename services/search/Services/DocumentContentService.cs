using Npgsql;
using SearchService.Models;

namespace SearchService.Services;

// Assembles DocumentResult objects from the documents table (content,
// populated by services/ingestion-resx) + graph_edges (dtcCodes) - the
// final response-shaping step once a search type has decided which
// id_documento(s) to return.
public class DocumentContentService
{
    private readonly string _connectionString;

    public DocumentContentService(IConfiguration configuration)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
    }

    // Returns results in the same order as idDocumenti (callers pass them
    // already ordered by reliability or vector distance). Documents with
    // no row for this language (or not found at all) are silently skipped.
    public async Task<List<DocumentResult>> GetDocumentResultsAsync(
        IReadOnlyCollection<string> idDocumenti, string language)
    {
        if (idDocumenti.Count == 0) return [];

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var byId = new Dictionary<string, DocumentResult>();
        await using (var cmd = new NpgsqlCommand("""
            SELECT id_documento, sigla_documento, titolo, impianto, dispositivo,
                   anomalia, causa, intervento, procedura, nota, reliability
            FROM documents
            WHERE language = @lang AND id_documento = ANY(@ids)
            """, conn))
        {
            cmd.Parameters.AddWithValue("lang", language);
            cmd.Parameters.AddWithValue("ids", idDocumenti.ToArray());
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetString(0);
                byId[id] = new DocumentResult
                {
                    IdDocumento = id,
                    SiglaDocumento = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Titolo = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Impianto = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Dispositivo = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Anomalia = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Causa = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    Intervento = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    Procedura = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    Nota = reader.IsDBNull(9) ? "" : reader.GetString(9),
                    Reliability = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                    Language = language,
                };
            }
        }

        await using (var cmd = new NpgsqlCommand("""
            SELECT from_id, to_id, description FROM graph_edges
            WHERE relation = 'CONTAINS_FAULT' AND language = @lang AND from_id = ANY(@ids)
            """, conn))
        {
            cmd.Parameters.AddWithValue("lang", language);
            cmd.Parameters.AddWithValue("ids", idDocumenti.ToArray());
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetString(0);
                if (byId.TryGetValue(id, out var doc))
                {
                    doc.DtcCodes.Add(new FaultCodeInfo
                    {
                        Code = reader.GetString(1),
                        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    });
                }
            }
        }

        return idDocumenti.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }
}
