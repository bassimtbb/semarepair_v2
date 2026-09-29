using Npgsql;
using SearchService.Models;

namespace SearchService.Services;

// Reads what the archive holds, so the interface can state it instead of
// claiming it. Three aggregates, no embedding, no Gemini call - this runs
// on every first page load and must stay cheap.
//
// Deliberately NOT filtered by vehicle: the help drawer opens before any
// vehicle is confirmed, and its first job is to say which vehicles exist
// at all. Once one is chosen the ordinary search paths take over, and they
// filter by engine code as strictly as ever.
public class CoverageService
{
    private readonly string _connectionString;

    public CoverageService(IConfiguration configuration)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
    }

    public async Task<CoverageResponse> GetAsync()
    {
        var response = new CoverageResponse();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // One row per distinct vehicle. gup_rows holds one row per
        // (vehicle, document) pair, so the same car repeats once per
        // document it owns - DISTINCT collapses that back to the car.
        await using (var cmd = new NpgsqlCommand("""
            SELECT DISTINCT
                   marca_macchina, modello_macchina, motorizzazione_macchina,
                   anno_inizio_macchina, anno_fine_macchina,
                   alimentazione_macchina, kw_macchina, cavalli_macchina,
                   codice_motore_macchina
            FROM gup_rows
            ORDER BY marca_macchina, modello_macchina, motorizzazione_macchina
            """, conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                response.Vehicles.Add(new CoverageVehicle
                {
                    Marca          = reader.IsDBNull(0) ? null : reader.GetString(0),
                    Modello        = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Motorizzazione = reader.IsDBNull(2) ? null : reader.GetString(2),
                    AnnoInizio     = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    AnnoFine       = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    Alimentazione  = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Kw             = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    Cavalli        = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    CodiceMotore   = reader.IsDBNull(8) ? null : reader.GetString(8),
                });
            }
        }

        // Repair sheets only. The same filter GraphSearchService applies in
        // GetFaultDocumentsForCarsAsync, so the figure shown to the client
        // and the figure the search can actually reach are the same one.
        await using (var cmd = new NpgsqlCommand("""
            SELECT count(*) FROM documents
            WHERE anomalia IS NOT NULL OR causa IS NOT NULL OR intervento IS NOT NULL
            """, conn))
        {
            response.RepairDocuments = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
        }

        // FILTER rather than four separate queries: one pass over a table
        // this size, and the four numbers are guaranteed to describe the
        // same instant.
        await using (var cmd = new NpgsqlCommand("""
            SELECT language,
                   count(*) FILTER (WHERE kind = 'fact')    AS facts,
                   count(*) FILTER (WHERE kind = 'section') AS procedures,
                   count(*) FILTER (WHERE kind = 'manual')  AS manual_pages,
                   count(DISTINCT asset_id)
                       FILTER (WHERE kind = 'legend' AND asset_id IS NOT NULL)
                       AS diagrams
            FROM knowledge_chunks
            GROUP BY language
            ORDER BY language
            """, conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
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

        // The same edge the fault-code search walks (CONTAINS_FAULT), so the
        // list offered and the list that can be answered are the same one by
        // construction, not by two definitions kept in step by hand.
        await using (var cmd = new NpgsqlCommand("""
            SELECT DISTINCT to_id
            FROM graph_edges
            WHERE relation = 'CONTAINS_FAULT' AND to_type = 'faultcode'
            ORDER BY to_id
            """, conn))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(0)) response.FaultCodes.Add(reader.GetString(0));
            }
        }

        return response;
    }
}
