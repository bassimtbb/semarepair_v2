using Npgsql;
using SearchService.Models;

namespace SearchService.Services;

// Graph traversal over graph_edges + gup_rows. See
// docs/SemaRepair_Architecture.md section 5.7/5.9 and section 6 of
// docs/EmbeddingAndGraph_Technical.md for the search-type SQL this mirrors.
public class GraphSearchService
{
    private readonly string _connectionString;
    private readonly ILogger<GraphSearchService> _logger;

    public GraphSearchService(IConfiguration configuration, ILogger<GraphSearchService> logger)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
        _logger = logger;
    }

    // Italian locative prepositions (su-/in- families). When a system/device
    // name is immediately preceded by one of these, the name is describing
    // WHERE something appears ("...sul quadro strumenti") rather than being the
    // subject of the fault - so it must NOT narrow the search. Subject
    // prepositions ("al", "del", "con") and subject position (start of the
    // phrase) deliberately fall through and DO narrow.
    private static readonly HashSet<string> ItalianLocativePrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "su", "sul", "sullo", "sulla", "sull", "sui", "sugli", "sulle",
        "in", "nel", "nello", "nella", "nell", "nei", "negli", "nelle",
        "sopra", "sotto", "dietro", "presso",
    };

    private NpgsqlConnection Connect() => new(_connectionString);

    // Resolves engine (+ optional brand) to the specific car(s) it refers
    // to. Engine code alone is not unique across brands in real data - see
    // the Marca comment on SearchRequest.
    public async Task<List<string>> ResolveCarIdsAsync(string codiceMotore, string? marca)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT id_macchina FROM gup_rows
            WHERE codice_motore_macchina = @codiceMotore
              AND (@marca::text IS NULL OR marca_macchina ILIKE @marca)
            """, conn);
        cmd.Parameters.AddWithValue("codiceMotore", codiceMotore);
        cmd.Parameters.AddWithValue("marca", marca ?? (object)DBNull.Value);
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
    // cleaned symptom text AS ITS SUBJECT? Returns the longest such match
    // (prefer specific over generic), or null if none do. Narrowing the
    // vector search to a matched device's documents is only correct when the
    // device is what the fault is ABOUT - a device named incidentally as a
    // location ("...la spia sul quadro strumenti") must not narrow, or an
    // engine fault silently gets restricted to instrument-cluster documents
    // (L3, confirmed live). For Italian this is done by rejecting a match
    // that is immediately preceded by a locative preposition; for other
    // languages the original whole-substring match is kept (cross-language
    // preposition ambiguity makes a universal guard unsafe - see progress.md
    // §28 for the residual risk).
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

        var isItalian = string.Equals(language, "it", StringComparison.OrdinalIgnoreCase);
        var textTokens = isItalian ? Tokenize(text) : null;

        var match = names
            .Where(name => isItalian
                ? AppearsAsSubject(textTokens!, name)
                : text.Contains(name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(name => name.Length)
            .FirstOrDefault();

        if (match is not null)
            _logger.LogInformation("Symptom search narrowed to system/device '{Match}' (query: '{Text}')", match, text);
        else
            _logger.LogInformation("Symptom search not narrowed - no system/device subject in '{Text}'", text);

        return match;
    }

    // True if `name` occurs in the tokenized text as a whole-word contiguous
    // sequence in a SUBJECT position - i.e. at least one occurrence is not
    // immediately preceded by a locative preposition. Whole-word tokens also
    // avoid substring-inside-a-word false matches.
    private static bool AppearsAsSubject(IReadOnlyList<string> textTokens, string name)
    {
        var nameTokens = Tokenize(name);
        if (nameTokens.Count == 0) return false;

        for (var p = 0; p + nameTokens.Count <= textTokens.Count; p++)
        {
            var matches = true;
            for (var k = 0; k < nameTokens.Count; k++)
                if (textTokens[p + k] != nameTokens[k]) { matches = false; break; }
            if (!matches) continue;

            // Occurrence found: subject unless the token right before it is a
            // locative preposition. Position 0 (no preceding word) is subject.
            if (p == 0 || !ItalianLocativePrepositions.Contains(textTokens[p - 1]))
                return true;
            // Otherwise this occurrence is incidental (locative); keep scanning
            // for a non-locative occurrence of the same name.
        }
        return false;
    }

    // Lowercased alphanumeric word tokens; every other character is a
    // separator. Applied to both the text and the name so they align.
    private static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        var sb = new System.Text.StringBuilder();
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    // Extracts the cars a matched document applies to, for a car_selection response.
    public async Task<List<CarSummary>> GetCarsForDocumentAsync(string idDocumento)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT gr.id_macchina, gr.marca_macchina, gr.modello_macchina,
                   gr.motorizzazione_macchina, gr.codice_motore_macchina,
                   gr.anno_inizio_macchina, gr.anno_fine_macchina,
                   gr.alimentazione_macchina, gr.kw_macchina, gr.cavalli_macchina
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
                Alimentazione = reader.IsDBNull(7) ? null : reader.GetString(7),
                Kw = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Cavalli = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            });
        }
        return results;
    }

    // Builds CarSummary objects for a raw list of car IDs (e.g. from
    // GetCarIdsForFaultAsync/GetCarIdsForKeywordAsync) - used for
    // car_selection responses that aren't derived from a single document.
    public async Task<List<CarSummary>> GetCarSummariesAsync(IReadOnlyCollection<string> carIds)
    {
        if (carIds.Count == 0) return [];
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT id_macchina, marca_macchina, modello_macchina,
                   motorizzazione_macchina, codice_motore_macchina,
                   anno_inizio_macchina, anno_fine_macchina,
                   alimentazione_macchina, kw_macchina, cavalli_macchina
            FROM gup_rows
            WHERE id_macchina = ANY(@carIds)
            """, conn);
        cmd.Parameters.AddWithValue("carIds", carIds.ToArray());

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
                Alimentazione = reader.IsDBNull(7) ? null : reader.GetString(7),
                Kw = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Cavalli = reader.IsDBNull(9) ? null : reader.GetInt32(9),
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
