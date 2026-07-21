using System.Globalization;
using Npgsql;

namespace SearchService.Services;

// Vector similarity over document_embeddings - Search Type 3 (symptom +
// confirmed car): rerank within a graph-prefiltered set of candidate
// documents. See docs/SemaRepair_Architecture.md section 6.8/8.3 and
// docs/EmbeddingAndGraph_Technical.md section 6 Search Type 3.
public class VectorSearchService
{
    private readonly string _connectionString;
    private readonly QueryEmbedder _embedder;

    public VectorSearchService(IConfiguration configuration, QueryEmbedder embedder)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
        _embedder = embedder;
    }

    // Ranks candidateIds by similarity to text, closest first (ascending
    // cosine distance). Only ever searches within the given candidate set -
    // this is what keeps Type 3 from returning a document for the wrong
    // car regardless of how the symptom is worded. Returns the full
    // ranking; how many of these count as "real" matches (Rule 10) is an
    // orchestration decision, not this service's concern.
    public async Task<List<(string IdDocumento, double Distance)>> RankWithinSetAsync(
        string text, IReadOnlyCollection<string> candidateIds, string language)
    {
        if (candidateIds.Count == 0) return [];

        var vector = await _embedder.EmbedAsync(text);
        var vectorLiteral = ToVectorLiteral(vector);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id_documento, embedding <=> @vec::vector AS dist
            FROM document_embeddings
            WHERE language = @lang AND id_documento = ANY(@candidateIds)
            ORDER BY dist, id_documento
            """, conn);
        cmd.Parameters.AddWithValue("vec", vectorLiteral);
        cmd.Parameters.AddWithValue("lang", language);
        cmd.Parameters.AddWithValue("candidateIds", candidateIds.ToArray());

        var results = new List<(string, double)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add((reader.GetString(0), reader.GetDouble(1)));
        return results;
    }

    private static string ToVectorLiteral(float[] vector) =>
        "[" + string.Join(",", vector.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]";
}
