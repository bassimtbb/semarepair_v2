using Npgsql;
using SearchService.Models;

namespace SearchService.Services;

// Graph traversal over graph_edges + gup_rows. See
// docs/SemaRepair_Architecture.md section 5.7/5.9 and section 6 of
// docs/EmbeddingAndGraph_Technical.md for the search-type SQL this mirrors.
public class GraphSearchService
{
    private readonly string _connectionString;

    public GraphSearchService(IConfiguration configuration)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
    }

    private NpgsqlConnection Connect() => new(_connectionString);

    // Resolves engine (+ optional brand) to the specific car(s) it refers
    // to. Engine code alone is not unique across brands in real data - see
    // the Brand comment on SearchRequest.
    public async Task<List<string>> ResolveCarIdsAsync(string engineCode, string? brand)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT id_macchina FROM gup_rows
            WHERE codice_motore_macchina = @engine
              AND (@brand::text IS NULL OR marca_macchina ILIKE @brand)
            """, conn);
        cmd.Parameters.AddWithValue("engine", engineCode);
        cmd.Parameters.AddWithValue("brand", brand ?? (object)DBNull.Value);
        return await ReadStringColumnAsync(cmd);
    }

    // Search Type 3 prefilter: every document linked to any of these cars
    // (DOCUMENTED_IN has no language column - caller filters by language
    // against document_embeddings/documents afterward).
    public async Task<List<string>> GetDocumentsForCarsAsync(IReadOnlyCollection<string> carIds)
    {
        if (carIds.Count == 0) return [];
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT to_id FROM graph_edges
            WHERE from_type = 'car' AND from_id = ANY(@carIds) AND relation = 'DOCUMENTED_IN'
            """, conn);
        cmd.Parameters.AddWithValue("carIds", carIds.ToArray());
        return await ReadStringColumnAsync(cmd);
    }

    // Search Type 1, car confirmed: docs linked to BOTH these cars AND this fault code.
    public async Task<List<string>> GetDocumentsForCarsAndFaultAsync(
        IReadOnlyCollection<string> carIds, string faultCode, string language)
    {
        if (carIds.Count == 0) return [];
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT to_id FROM graph_edges
            WHERE from_type = 'car' AND from_id = ANY(@carIds) AND relation = 'DOCUMENTED_IN'
            INTERSECT
            SELECT from_id FROM graph_edges
            WHERE to_type = 'faultcode' AND to_id = @code AND relation = 'CONTAINS_FAULT' AND language = @lang
            """, conn);
        cmd.Parameters.AddWithValue("carIds", carIds.ToArray());
        cmd.Parameters.AddWithValue("code", faultCode);
        cmd.Parameters.AddWithValue("lang", language);
        return await ReadStringColumnAsync(cmd);
    }

    // Documents containing this fault code, regardless of car - used both
    // for Rule 3 (no car confirmed) and for determining the fault's System
    // when checking Rule 8 fallback eligibility.
    public async Task<List<string>> GetDocumentsForFaultAsync(string faultCode, string language)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT from_id FROM graph_edges
            WHERE to_type = 'faultcode' AND to_id = @code AND relation = 'CONTAINS_FAULT' AND language = @lang
            """, conn);
        cmd.Parameters.AddWithValue("code", faultCode);
        cmd.Parameters.AddWithValue("lang", language);
        return await ReadStringColumnAsync(cmd);
    }

    // Rule 3: fault code, no car confirmed -> cars whose documents include this fault.
    public async Task<List<string>> GetCarIdsForFaultAsync(string faultCode, string language)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT ge.from_id FROM graph_edges ge
            WHERE ge.from_type = 'car' AND ge.relation = 'DOCUMENTED_IN'
              AND ge.to_id IN (
                  SELECT from_id FROM graph_edges
                  WHERE to_type = 'faultcode' AND to_id = @code AND relation = 'CONTAINS_FAULT' AND language = @lang
              )
            """, conn);
        cmd.Parameters.AddWithValue("code", faultCode);
        cmd.Parameters.AddWithValue("lang", language);
        return await ReadStringColumnAsync(cmd);
    }

    // Search Type 2, car confirmed: docs linked to BOTH these cars AND this
    // system/device name (checks both AFFECTS_SYSTEM and INVOLVES_DEVICE -
    // the "system" endpoint's name param is documented generically as
    // "system/device keyword").
    public async Task<List<string>> GetDocumentsForCarsAndKeywordAsync(
        IReadOnlyCollection<string> carIds, string keyword, string language)
    {
        if (carIds.Count == 0) return [];
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT to_id FROM graph_edges
            WHERE from_type = 'car' AND from_id = ANY(@carIds) AND relation = 'DOCUMENTED_IN'
            INTERSECT
            SELECT from_id FROM graph_edges
            WHERE relation IN ('AFFECTS_SYSTEM', 'INVOLVES_DEVICE') AND to_id ILIKE @keyword AND language = @lang
            """, conn);
        cmd.Parameters.AddWithValue("carIds", carIds.ToArray());
        cmd.Parameters.AddWithValue("keyword", keyword);
        cmd.Parameters.AddWithValue("lang", language);
        return await ReadStringColumnAsync(cmd);
    }

    // Documents matching this system/device name, regardless of car. Used
    // for the no-car "system" endpoint and to narrow candidates in Search
    // Type 4 (symptom, no car) once a System/Device match is found.
    public async Task<List<string>> GetDocumentsForKeywordAsync(string keyword, string language)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT from_id FROM graph_edges
            WHERE relation IN ('AFFECTS_SYSTEM', 'INVOLVES_DEVICE') AND to_id ILIKE @keyword AND language = @lang
            """, conn);
        cmd.Parameters.AddWithValue("keyword", keyword);
        cmd.Parameters.AddWithValue("lang", language);
        return await ReadStringColumnAsync(cmd);
    }

    // No car confirmed, system/device keyword -> cars whose documents match it.
    public async Task<List<string>> GetCarIdsForKeywordAsync(string keyword, string language)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT ge.from_id FROM graph_edges ge
            WHERE ge.from_type = 'car' AND ge.relation = 'DOCUMENTED_IN'
              AND ge.to_id IN (
                  SELECT from_id FROM graph_edges
                  WHERE relation IN ('AFFECTS_SYSTEM', 'INVOLVES_DEVICE') AND to_id ILIKE @keyword AND language = @lang
              )
            """, conn);
        cmd.Parameters.AddWithValue("keyword", keyword);
        cmd.Parameters.AddWithValue("lang", language);
        return await ReadStringColumnAsync(cmd);
    }

    // Search Type 4 step 2: does any System/Device name appear in the
    // cleaned symptom text? Returns the longest match (prefer specific over
    // generic when more than one name matches), or null if none do.
    public async Task<string?> MatchSystemOrDeviceAsync(string text, string language)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT to_id FROM graph_edges
            WHERE relation IN ('AFFECTS_SYSTEM', 'INVOLVES_DEVICE') AND language = @lang
            """, conn);
        cmd.Parameters.AddWithValue("lang", language);
        var names = await ReadStringColumnAsync(cmd);

        return names
            .Where(name => text.Contains(name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(name => name.Length)
            .FirstOrDefault();
    }

    // Extracts the cars a matched document applies to, for a car_selection response.
    public async Task<List<CarSummary>> GetCarsForDocumentAsync(string idDocumento)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT gr.id_macchina, gr.marca_macchina, gr.modello_macchina,
                   gr.motorizzazione_macchina, gr.codice_motore_macchina,
                   gr.anno_inizio_macchina, gr.anno_fine_macchina
            FROM graph_edges ge
            JOIN gup_rows gr ON gr.id_macchina = ge.from_id
            WHERE ge.from_type = 'car' AND ge.to_type = 'document'
              AND ge.to_id = @doc AND ge.relation = 'DOCUMENTED_IN'
            """, conn);
        cmd.Parameters.AddWithValue("doc", idDocumento);

        var results = new List<CarSummary>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new CarSummary
            {
                IdMacchina = reader.GetString(0),
                Marca = reader.GetString(1),
                Modello = reader.GetString(2),
                Motorizzazione = reader.IsDBNull(3) ? null : reader.GetString(3),
                CodiceMotore = reader.GetString(4),
                AnnoInizio = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                AnnoFine = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            });
        }
        return results;
    }

    // Rule 8 fallback: other cars sharing this car's engine code (any brand).
    public async Task<List<string>> GetSharedEngineCarIdsAsync(string carId)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT to_id FROM graph_edges
            WHERE from_type = 'car' AND from_id = @carId AND relation = 'SHARES_ENGINE_WITH'
            """, conn);
        cmd.Parameters.AddWithValue("carId", carId);
        return await ReadStringColumnAsync(cmd);
    }

    // Which System(s) a fault code's documents affect - used to check Rule 8
    // eligibility (Motore/Elettrico motore categories only) before falling back.
    public async Task<List<string>> GetSystemsForFaultAsync(string faultCode, string language)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT to_id FROM graph_edges
            WHERE relation = 'AFFECTS_SYSTEM' AND language = @lang
              AND from_id IN (
                  SELECT from_id FROM graph_edges
                  WHERE to_type = 'faultcode' AND to_id = @code AND relation = 'CONTAINS_FAULT' AND language = @lang
              )
            """, conn);
        cmd.Parameters.AddWithValue("code", faultCode);
        cmd.Parameters.AddWithValue("lang", language);
        return await ReadStringColumnAsync(cmd);
    }

    private static async Task<List<string>> ReadStringColumnAsync(NpgsqlCommand cmd)
    {
        var results = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(reader.GetString(0));
        return results;
    }
}
