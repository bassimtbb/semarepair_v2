using System.Globalization;
using Npgsql;

namespace SearchService.Services;

// Vector similarity over symptom_embeddings (anomalia field only) - the
// no-car entry point (Search Type 4). See docs/SemaRepair_Architecture.md
// section 5.6 Query 4 and docs/EmbeddingAndGraph_Technical.md section 4 for
// why anomalia-only embeddings are more precise here than full document
// embeddings: short, precise text gives a much bigger gap between the
// correct match and the next candidate.
public class SymptomSearchService
{
    private readonly string _connectionString;
    private readonly QueryEmbedder _embedder;

    public SymptomSearchService(IConfiguration configuration, QueryEmbedder embedder)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
        _embedder = embedder;
    }

    // Ranks every document in symptom_embeddings for this language by
    // similarity to text, closest first. The doc's own SQL only ever uses
    // LIMIT 1 (it just needs the single best match to seed a car_selection
    // list), but returning a small ranked list costs nothing extra and
    // lets the caller inspect the distance gap if needed.
    //
    // candidateIds, if given, restricts the search to that set - used when
    // Search Type 4 finds a System/Device match first: the doc's own prose
    // says graph narrows the system and vector narrows the specific
    // symptom within it, even though its literal SQL example doesn't show
    // this restriction. Null searches the whole table (no system matched).
    public async Task<List<(string IdDocumento, double Distance)>> FindBestMatchesAsync(
        string text, string language, IReadOnlyCollection<string>? candidateIds = null, int limit = 5)
    {
        if (candidateIds is { Count: 0 }) return [];

        var vector = await _embedder.EmbedAsync(text);
        var vectorLiteral = ToVectorLiteral(vector);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"""
            SELECT id_documento, embedding <=> @vec::vector AS dist
            FROM symptom_embeddings
            WHERE language = @lang
              {(candidateIds is null ? "" : "AND id_documento = ANY(@candidateIds)")}
            ORDER BY dist
            LIMIT @limit
            """, conn);
        cmd.Parameters.AddWithValue("vec", vectorLiteral);
        cmd.Parameters.AddWithValue("lang", language);
        cmd.Parameters.AddWithValue("limit", limit);
        if (candidateIds is not null)
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
