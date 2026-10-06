using Npgsql;
using SearchService.Models;

namespace SearchService.Services;

// Reads what the archive holds, per vehicle, so the interface can state it
// instead of claiming it. No embedding, no Gemini call - this runs on every
// first page load and must stay cheap.
public class CoverageService
{
    private const int ExamplesPerSection = 4;

    private readonly string _connectionString;
    private readonly string _assetsPath;
    private readonly ILogger<CoverageService> _logger;

    public CoverageService(IConfiguration configuration, ILogger<CoverageService> logger)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
        _assetsPath = configuration["SEMAREPAIR_ASSETS_PATH"] ?? "/app/Assets";
        _logger = logger;
    }

    // Which pictures and drawings were REALLY delivered, by id.
    //
    // "The chunk has an asset_id" and "there is a file to show" are two
    // different questions, and the gap between them is not small: 212 of the
    // assets the documents reference were never sent. Suggesting a question
    // whose answer turns out to have no picture is worse than suggesting
    // nothing, so the filesystem - not the column - decides what goes in the
    // photo and diagram sections.
    private (string[] Photos, string[] Diagrams) DeliveredAssets()
    {
        return (Ids("photos"), Ids("pdf"));

        string[] Ids(string folder)
        {
            var dir = Path.Combine(_assetsPath, folder);
            if (!Directory.Exists(dir))
            {
                // Not fatal. The sections that depend on it come back empty,
                // the drawer then shows no photo section, and every other
                // part of the answer is unaffected.
                _logger.LogWarning("Asset directory {Dir} not found - the {Folder} section will be empty", dir, folder);
                return [];
            }

            return Directory.EnumerateFiles(dir)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .Distinct()
                .ToArray();
        }
    }

    public async Task<CoverageResponse> GetAsync(string language)
    {
        var response = new CoverageResponse();
        var (photoIds, diagramIds) = DeliveredAssets();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var vehicles = await ReadVehiclesAsync(conn);
        foreach (var v in vehicles) response.Vehicles.Add(v);

        var byId = vehicles.ToDictionary(v => v.IdMacchina);

        await FillCasesAsync(conn, byId, language);
        await FillFaultCodesAsync(conn, byId, language);
        await FillChunkSectionAsync(conn, byId, language, photoIds, diagramIds);

        await ReadLanguagesAsync(conn, response);
        return response;
    }

    // gup_rows holds one row per (vehicle, document) pair, so the same car
    // repeats once per document it owns - DISTINCT collapses that back.
    private static async Task<List<CoverageVehicle>> ReadVehiclesAsync(NpgsqlConnection conn)
    {
        var list = new List<CoverageVehicle>();

        await using var cmd = new NpgsqlCommand("""
            SELECT DISTINCT
                   id_macchina, marca_macchina, modello_macchina, motorizzazione_macchina,
                   anno_inizio_macchina, anno_fine_macchina, alimentazione_macchina,
                   kw_macchina, cavalli_macchina, codice_motore_macchina
            FROM gup_rows
            ORDER BY marca_macchina, modello_macchina, motorizzazione_macchina
            """, conn);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var v = new CoverageVehicle
            {
                IdMacchina     = reader.GetString(0),
                Marca          = reader.IsDBNull(1) ? null : reader.GetString(1),
                Modello        = reader.IsDBNull(2) ? null : reader.GetString(2),
                Motorizzazione = reader.IsDBNull(3) ? null : reader.GetString(3),
                AnnoInizio     = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                AnnoFine       = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                Alimentazione  = reader.IsDBNull(6) ? null : reader.GetString(6),
                Kw             = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Cavalli        = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                CodiceMotore   = reader.IsDBNull(9) ? null : reader.GetString(9),
            };
            v.Query = string.Join(' ', new[] { v.Marca, v.Modello, v.Motorizzazione }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            list.Add(v);
        }

        return list;
    }

    // Repair sheets. The examples are anomalia texts verbatim - what the
    // mechanic would type, in the archive's own words.
    private static async Task FillCasesAsync(
        NpgsqlConnection conn, Dictionary<string, CoverageVehicle> byId, string language)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT g.id_macchina,
                   count(DISTINCT d.id_documento),
                   (array_agg(DISTINCT d.anomalia))[1:{ExamplesPerSection}]
            FROM gup_rows g
            JOIN documents d ON d.id_documento = g.id_documento AND d.language = @lang
            WHERE d.anomalia IS NOT NULL AND length(trim(d.anomalia)) > 0
            GROUP BY g.id_macchina
            """, conn);
        cmd.Parameters.AddWithValue("lang", language);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!byId.TryGetValue(reader.GetString(0), out var v)) continue;
            v.Sections.Cases.Total = reader.GetInt64(1);
            v.Sections.Cases.Examples = Clean(reader.GetFieldValue<string[]>(2));
        }
    }

    // Every code, not a sample: they are short, the drawer lists them all
    // grouped by family, and "which codes does THIS car answer" is the whole
    // point - a tester who types one at random gets a correct "not found"
    // and concludes the product is broken.
    private static async Task FillFaultCodesAsync(
        NpgsqlConnection conn, Dictionary<string, CoverageVehicle> byId, string language)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT g.id_macchina, count(DISTINCT e.to_id),
                   array_agg(DISTINCT e.to_id ORDER BY e.to_id)
            FROM gup_rows g
            JOIN graph_edges e ON e.from_id = g.id_documento
                              AND e.relation = 'CONTAINS_FAULT'
                              AND e.from_type = 'document'
                              AND e.language = @lang
            GROUP BY g.id_macchina
            """, conn);
        cmd.Parameters.AddWithValue("lang", language);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!byId.TryGetValue(reader.GetString(0), out var v)) continue;
            v.Sections.FaultCodes.Total = reader.GetInt64(1);
            v.Sections.FaultCodes.Examples = Clean(reader.GetFieldValue<string[]>(2));
        }
    }

    // Photos, diagrams, manual pages and plain technical data in one pass.
    //
    // The delivered-asset ids go INTO the query rather than filtering its
    // result in memory: there are about a hundred of them against nine
    // thousand chunks, so the small set is the one that should travel.
    private static async Task FillChunkSectionAsync(
        NpgsqlConnection conn, Dictionary<string, CoverageVehicle> byId, string language,
        string[] photoIds, string[] diagramIds)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT g.id_macchina,
                   CASE
                     WHEN k.kind = 'manual'                                      THEN 'manual'
                     WHEN k.kind = 'legend' AND k.asset_id = ANY(@diagrams)      THEN 'diagram'
                     WHEN k.kind <> 'legend' AND k.asset_id = ANY(@photos)       THEN 'photo'
                     WHEN k.kind = 'fact'                                        THEN 'technical'
                     ELSE 'other'
                   END AS bucket,
                   count(*),
                   count(DISTINCT k.asset_id),
                   (array_agg(DISTINCT coalesce(nullif(k.heading, ''), k.label)))[1:{ExamplesPerSection}]
            FROM gup_rows g
            JOIN knowledge_chunks k ON k.id_documento = g.id_documento AND k.language = @lang
            GROUP BY 1, 2
            """, conn);
        cmd.Parameters.AddWithValue("lang", language);
        cmd.Parameters.AddWithValue("photos", photoIds);
        cmd.Parameters.AddWithValue("diagrams", diagramIds);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!byId.TryGetValue(reader.GetString(0), out var v)) continue;

            var section = reader.GetString(1) switch
            {
                "manual"    => v.Sections.Manual,
                "diagram"   => v.Sections.Diagrams,
                "photo"     => v.Sections.Photos,
                "technical" => v.Sections.Technical,
                _           => null,
            };
            if (section is null) continue;

            // Photos and diagrams are counted by DISTINCT asset, everything
            // else by chunk. One diagram contributes a legend row per
            // component - 43 for the injection schematic alone - so counting
            // rows told the mechanic the Fiat had 185 wiring diagrams when it
            // has 11. The unit a section announces has to be the unit the
            // mechanic will see on screen.
            var byAsset = reader.GetString(1) is "photo" or "diagram";
            section.Total = byAsset ? reader.GetInt64(3) : reader.GetInt64(2);
            section.Examples = reader.IsDBNull(4) ? [] : Clean(reader.GetFieldValue<string?[]>(4));
        }
    }

    private static async Task ReadLanguagesAsync(NpgsqlConnection conn, CoverageResponse response)
    {
        // FILTER rather than four queries: one pass, and the four numbers
        // are guaranteed to describe the same instant.
        await using var cmd = new NpgsqlCommand("""
            SELECT language,
                   count(*) FILTER (WHERE kind = 'fact')    AS facts,
                   count(*) FILTER (WHERE kind = 'section') AS procedures,
                   count(*) FILTER (WHERE kind = 'manual')  AS manual_pages,
                   count(DISTINCT asset_id)
                       FILTER (WHERE kind = 'legend' AND asset_id IS NOT NULL) AS diagrams
            FROM knowledge_chunks
            GROUP BY language
            ORDER BY language
            """, conn);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            response.Languages.Add(new CoverageLanguage
            {
                Code        = reader.GetString(0),
                Facts       = reader.GetInt64(1),
                Procedures  = reader.GetInt64(2),
                ManualPages = reader.GetInt64(3),
                Diagrams    = reader.GetInt64(4),
            });
        }
    }

    // Postgres array_agg keeps NULLs and the archive keeps whitespace; a
    // blank chip is worse than one suggestion fewer.
    private static List<string> Clean(IEnumerable<string?> values) =>
        values.Where(s => !string.IsNullOrWhiteSpace(s))
              .Select(s => s!.Trim())
              .ToList();
}
