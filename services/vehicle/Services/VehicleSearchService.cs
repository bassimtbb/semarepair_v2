using Npgsql;
using VehicleService.Models;

namespace VehicleService.Services;

// Structured SQL car identification. See docs/SemaRepair_Architecture.md section 6.5.
// Currently backed by Our PostgreSQL gup_rows (the local stand-in for Their SQL
// Server) until production database access is granted - see section 8.1.
public class VehicleSearchService
{
    private readonly string _connectionString;

    public VehicleSearchService(IConfiguration configuration)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
    }

    private NpgsqlConnection Connect() => new(_connectionString);

    private const string SelectColumns = """
        SELECT DISTINCT id_macchina, marca_macchina, modello_macchina,
               motorizzazione_macchina, codice_motore_macchina, alimentazione_macchina,
               anno_inizio_macchina, anno_fine_macchina, kw_macchina, cavalli_macchina
        FROM gup_rows
        """;

    // gup_rows is one row per (car, document) pair, so every query here needs
    // DISTINCT to avoid returning the same car once per linked document - see
    // the same bug fixed in GraphSearchService.
    public async Task<VehicleResponse> SearchAsync(VehicleQuery query)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"""
            {SelectColumns}
            WHERE (@marca::text IS NULL OR marca_macchina ILIKE @marca)
              AND (@modello::text IS NULL OR modello_macchina ILIKE @modello)
              AND (@alimentazione::text IS NULL OR alimentazione_macchina ILIKE @alimentazione)
              AND (@motorizzazione::text IS NULL OR motorizzazione_macchina ILIKE @motorizzazione)
              AND (@codiceMotore::text IS NULL OR codice_motore_macchina = @codiceMotore)
              AND (@kw::int IS NULL OR kw_macchina = @kw)
              AND (@annoInizio::int IS NULL OR anno_fine_macchina IS NULL OR anno_fine_macchina >= @annoInizio)
              AND (@annoFine::int IS NULL OR anno_inizio_macchina IS NULL OR anno_inizio_macchina <= @annoFine)
            """, conn);
        cmd.Parameters.AddWithValue("marca", query.Marca ?? (object)DBNull.Value);
        // Wrapped in wildcards (unlike marca/alimentazione, which rely on the caller
        // passing the exact brand/fuel text) - real bug found via a real query
        // ("ho un iveco daily con motore 8140.43S"): the mechanic naturally says
        // "Daily", but gup_rows stores "Daily III", and an exact ILIKE match
        // returned zero rows despite codiceMotore alone correctly identifying all
        // 5 trims. Accepted tradeoff, same as motorizzazione below: this can
        // over-match (e.g. "Focus" also matching the unrelated "Focus C-Max"
        // model), but FindCar already returns every match as a selectable card
        // list, so a mechanic seeing one extra card they can ignore is far better
        // than a real car returning zero results at all.
        cmd.Parameters.AddWithValue("modello", query.Modello is null ? (object)DBNull.Value : $"%{query.Modello}%");
        cmd.Parameters.AddWithValue("alimentazione", query.Alimentazione ?? (object)DBNull.Value);
        // Wrapped in wildcards (unlike marca/alimentazione, which rely on the
        // caller passing the exact brand/fuel text) - a mechanic's free-text engine
        // label ("1.5 TDCi", "1.5 TDCi Euro 5") is rarely the full stored string verbatim.
        cmd.Parameters.AddWithValue("motorizzazione", query.Motorizzazione is null ? (object)DBNull.Value : $"%{query.Motorizzazione}%");
        cmd.Parameters.AddWithValue("codiceMotore", query.CodiceMotore ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("kw", query.Kw ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("annoInizio", query.AnnoInizio ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("annoFine", query.AnnoFine ?? (object)DBNull.Value);

        var cars = await ReadCarsAsync(cmd);

        int? suggestedYearFrom = null;
        int? suggestedYearTo = null;
        // Only engage this fallback when the original request actually had
        // a year filter and still came back empty - a plain wrong
        // brand/model with no year filter at all should stay a simple
        // "not found" (count 0, no suggestion), not run an extra query for
        // no reason.
        if (cars.Count == 0 && (query.AnnoInizio.HasValue || query.AnnoFine.HasValue))
        {
            (suggestedYearFrom, suggestedYearTo) = await ComputeSuggestedYearRangeAsync(conn, query);
        }

        return new VehicleResponse
        {
            Count = cars.Count,
            Cars = cars,
            SuggestedYearFrom = suggestedYearFrom,
            SuggestedYearTo = suggestedYearTo,
        };
    }

    // Same marca/modello/alimentazione/motorizzazione/codiceMotore/kw
    // filters as the main query, deliberately without the year constraint -
    // tells the caller whether the brand+model exist at all (count > 0)
    // and, if so, the real production year range to suggest, computed
    // directly from gup_rows rather than approximated. anno_fine_macchina
    // can be 9999 ("still in production, no end year yet") - returned as-is
    // here; turning that into a sensible "to today" phrase is Chat
    // Service's concern, not this query's.
    private static async Task<(int? From, int? To)> ComputeSuggestedYearRangeAsync(NpgsqlConnection conn, VehicleQuery query)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT MIN(anno_inizio_macchina), MAX(anno_fine_macchina), COUNT(*)
            FROM gup_rows
            WHERE (@marca::text IS NULL OR marca_macchina ILIKE @marca)
              AND (@modello::text IS NULL OR modello_macchina ILIKE @modello)
              AND (@alimentazione::text IS NULL OR alimentazione_macchina ILIKE @alimentazione)
              AND (@motorizzazione::text IS NULL OR motorizzazione_macchina ILIKE @motorizzazione)
              AND (@codiceMotore::text IS NULL OR codice_motore_macchina = @codiceMotore)
              AND (@kw::int IS NULL OR kw_macchina = @kw)
            """, conn);
        cmd.Parameters.AddWithValue("marca", query.Marca ?? (object)DBNull.Value);
        // Same wildcard-wrap and same reasoning as the main query above.
        cmd.Parameters.AddWithValue("modello", query.Modello is null ? (object)DBNull.Value : $"%{query.Modello}%");
        cmd.Parameters.AddWithValue("alimentazione", query.Alimentazione ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("motorizzazione", query.Motorizzazione is null ? (object)DBNull.Value : $"%{query.Motorizzazione}%");
        cmd.Parameters.AddWithValue("codiceMotore", query.CodiceMotore ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("kw", query.Kw ?? (object)DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.GetInt64(2) == 0)
            return (null, null);

        return (
            reader.IsDBNull(0) ? null : reader.GetInt32(0),
            reader.IsDBNull(1) ? null : reader.GetInt32(1));
    }

    public async Task<VehicleResult?> GetByIdAsync(string idMacchina)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"""
            {SelectColumns}
            WHERE id_macchina = @id
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("id", idMacchina);

        var cars = await ReadCarsAsync(cmd);
        return cars.Count > 0 ? cars[0] : null;
    }

    private static async Task<List<VehicleResult>> ReadCarsAsync(NpgsqlCommand cmd)
    {
        var results = new List<VehicleResult>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new VehicleResult
            {
                IdMacchina = reader.GetString(0),
                Marca = reader.GetString(1),
                Modello = reader.GetString(2),
                Motorizzazione = reader.IsDBNull(3) ? null : reader.GetString(3),
                CodiceMotore = reader.GetString(4),
                Alimentazione = reader.IsDBNull(5) ? null : reader.GetString(5),
                AnnoInizio = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                AnnoFine = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Kw = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Cavalli = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            });
        }
        return results;
    }
}
