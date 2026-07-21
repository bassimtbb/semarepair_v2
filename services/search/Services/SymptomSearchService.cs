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
    // Tolerance for comparing a distance already round-tripped through C#
    // against one PostgreSQL computes fresh in the tie re-query - exact
    // equality between the two is fragile and would silently return zero
    // rows (and re-drop the tied document) on any rounding mismatch.
    private const double DistanceEpsilon = 1e-9;

    private readonly string _connectionString;
    private readonly QueryEmbedder _embedder;
    private readonly ILogger<SymptomSearchService> _logger;

    public SymptomSearchService(IConfiguration configuration, QueryEmbedder embedder, ILogger<SymptomSearchService> logger)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
        _embedder = embedder;
        _logger = logger;
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

        // Fetch one row past the limit - it's the cheapest way to tell
        // whether the cutoff falls in the middle of a tie. Two documents
        // with the exact same distance sit right at the boundary as often
        // as not (duplicate symptom text, near-duplicate documents), and
        // without this probe row LIMIT would silently keep one and drop
        // the other based on nothing but id_documento ordering.
        var results = await QueryRankedAsync(conn, vectorLiteral, language, candidateIds, limit + 1);

        if (results.Count <= limit)
            return results;

        var cutoffDist = results[limit - 1].Distance;
        if (results[limit].Distance > cutoffDist + DistanceEpsilon)
        {
            results.RemoveAt(limit);
            return results;
        }

        // Boundary tie: the row just past the limit shares the cutoff
        // distance, so more documents are tied there than this LIMIT
        // window can show us. Re-query with no LIMIT for every document at
        // or under the cutoff instead of silently truncating the tie.
        var tied = await QueryTiedAsync(conn, vectorLiteral, language, candidateIds, cutoffDist);
        _logger.LogInformation(
            "Boundary tie detected at distance {Dist}; re-query returned {Count} tied documents",
            cutoffDist, tied.Count);

        var merged = results.Where(r => r.Distance < cutoffDist - DistanceEpsilon).ToList();
        merged.AddRange(tied);
        return merged
            .GroupBy(r => r.IdDocumento)
            .Select(g => g.First())
            .OrderBy(r => r.Distance)
            .ThenBy(r => r.IdDocumento, StringComparer.Ordinal)
            .ToList();
    }

    private static async Task<List<(string IdDocumento, double Distance)>> QueryRankedAsync(
        NpgsqlConnection conn, string vectorLiteral, string language,
        IReadOnlyCollection<string>? candidateIds, int limit)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT id_documento, embedding <=> @vec::vector AS dist
            FROM symptom_embeddings
            WHERE language = @lang
              {(candidateIds is null ? "" : "AND id_documento = ANY(@candidateIds)")}
            ORDER BY dist, id_documento
            LIMIT @limit
            """, conn);
        cmd.Parameters.AddWithValue("vec", vectorLiteral);
        cmd.Parameters.AddWithValue("lang", language);
        cmd.Parameters.AddWithValue("limit", limit);
        if (candidateIds is not null)
            cmd.Parameters.AddWithValue("candidateIds", candidateIds.ToArray());

        return await ReadResultsAsync(cmd);
    }

    // No LIMIT - the whole point is to never truncate a tie, however wide
    // it turns out to be.
    private static async Task<List<(string IdDocumento, double Distance)>> QueryTiedAsync(
        NpgsqlConnection conn, string vectorLiteral, string language,
        IReadOnlyCollection<string>? candidateIds, double cutoffDist)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT id_documento, embedding <=> @vec::vector AS dist
            FROM symptom_embeddings
            WHERE language = @lang
              AND embedding <=> @vec::vector <= @cutoffDist + 1e-9
              {(candidateIds is null ? "" : "AND id_documento = ANY(@candidateIds)")}
            ORDER BY dist, id_documento
            """, conn);
        cmd.Parameters.AddWithValue("vec", vectorLiteral);
        cmd.Parameters.AddWithValue("lang", language);
        cmd.Parameters.AddWithValue("cutoffDist", cutoffDist);
        if (candidateIds is not null)
            cmd.Parameters.AddWithValue("candidateIds", candidateIds.ToArray());

        return await ReadResultsAsync(cmd);
    }

    private static async Task<List<(string IdDocumento, double Distance)>> ReadResultsAsync(NpgsqlCommand cmd)
    {
        var results = new List<(string, double)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add((reader.GetString(0), reader.GetDouble(1)));
        return results;
    }

    private static string ToVectorLiteral(float[] vector) =>
        "[" + string.Join(",", vector.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]";
}
