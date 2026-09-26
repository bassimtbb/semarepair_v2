using System.Globalization;
using Npgsql;
using SearchService.Models;

namespace SearchService.Services;

// Vector similarity over knowledge_chunks - the extension's search path
// (docs/Architecture_Extension_v2.md section 3). Answers "which fuse",
// "what torque", "where is this component", "show me the wiring diagram" -
// questions no repair sheet contains.
//
// Separate from VectorSearchService on purpose. That one reranks inside a
// graph-prefiltered candidate set for Search Type 3; this one filters by
// vehicle in SQL and ranks the whole chunk table for that vehicle in a
// single query. Sharing the class would have meant a mode flag on a service
// that currently has one clear job.
public class TechnicalSearchService
{
    private readonly string _connectionString;
    private readonly QueryEmbedder _embedder;

    public TechnicalSearchService(IConfiguration configuration, QueryEmbedder embedder)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
        _embedder = embedder;
    }

    // codiceMotore is required by the caller, never optional: a torque
    // figure or a fuse rating is specific to one vehicle, and answering
    // with another one's is worse than answering nothing. Same rule the
    // document endpoints already enforce (M1 / Rule 1).
    //
    // The vehicle filter mirrors GraphSearchService.ResolveCarIdsAsync
    // exactly - engine code, optionally narrowed by brand, because one
    // engine code spans several brands in real data.
    public async Task<List<TechnicalChunk>> SearchAsync(
        string query, string codiceMotore, string? marca, string language, int limit)
    {
        var vector = await _embedder.EmbedAsync(query);
        var vectorLiteral = ToVectorLiteral(vector);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT kc.id_documento, kc.language, kc.kind, kc.heading, kc.label,
                   kc.value, kc.unit, kc.reference, kc.body, kc.asset_id,
                   d.titolo,
                   kc.embedding <=> @vec::vector AS dist
            FROM knowledge_chunks kc
            LEFT JOIN documents d
              ON d.id_documento = kc.id_documento AND d.language = kc.language
            WHERE kc.language = @lang
              AND kc.embedding IS NOT NULL
              AND kc.id_documento IN (
                  SELECT DISTINCT id_documento FROM gup_rows
                  WHERE codice_motore_macchina = @codiceMotore
                    AND (@marca::text IS NULL OR marca_macchina ILIKE @marca)
              )
            ORDER BY dist, kc.id, kc.id_documento
            LIMIT @limit
            """, conn);
        cmd.Parameters.AddWithValue("vec", vectorLiteral);
        cmd.Parameters.AddWithValue("lang", language);
        cmd.Parameters.AddWithValue("codiceMotore", codiceMotore);
        cmd.Parameters.AddWithValue("marca", (object?)marca ?? DBNull.Value);
        cmd.Parameters.AddWithValue("limit", limit);

        var results = new List<TechnicalChunk>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new TechnicalChunk
            {
                IdDocumento   = reader.GetString(0),
                Language      = reader.GetString(1),
                Kind          = reader.GetString(2),
                Heading       = reader.IsDBNull(3) ? null : reader.GetString(3),
                Label         = reader.IsDBNull(4) ? null : reader.GetString(4),
                Value         = reader.IsDBNull(5) ? null : reader.GetString(5),
                Unit          = reader.IsDBNull(6) ? null : reader.GetString(6),
                Reference     = reader.IsDBNull(7) ? null : reader.GetString(7),
                Body          = reader.IsDBNull(8) ? null : reader.GetString(8),
                AssetId       = reader.IsDBNull(9) ? null : reader.GetString(9),
                DocumentTitle = reader.IsDBNull(10) ? null : reader.GetString(10),
                Distance      = reader.GetDouble(11),
            });
        }
        return results;
    }

    private static string ToVectorLiteral(float[] vector) =>
        "[" + string.Join(",", vector.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]";
}
