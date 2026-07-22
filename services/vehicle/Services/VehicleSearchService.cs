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

    // Shared filter on marca/modello/alimentazione/motorizzazione/codiceMotore/kw.
    // motorizzazione is matched token-by-token (see @motorizzazioneTokens below),
    // not as one literal substring - the year clauses are appended per-query
    // since the year-suggestion query deliberately omits them.
    private const string CommonWhere = """
        WHERE (@marca::text IS NULL OR marca_macchina ILIKE @marca)
          AND (@modello::text IS NULL OR modello_macchina ILIKE @modello)
          AND (@alimentazione::text IS NULL OR alimentazione_macchina ILIKE @alimentazione)
          AND (@motorizzazioneTokens::text[] IS NULL
               OR NOT EXISTS (
                    SELECT 1 FROM unnest(@motorizzazioneTokens) AS tok
                    WHERE motorizzazione_macchina IS NULL
                       OR motorizzazione_macchina NOT ILIKE '%' || tok || '%'))
          AND (@codiceMotore::text IS NULL OR codice_motore_macchina = @codiceMotore)
          AND (@kw::int IS NULL OR kw_macchina = @kw)
        """;

    // gup_rows is one row per (car, document) pair, so every query here needs
    // DISTINCT to avoid returning the same car once per linked document - see
    // the same bug fixed in GraphSearchService.
    public async Task<VehicleResponse> SearchAsync(VehicleQuery query)
    {
        await using var conn = Connect();
        await conn.OpenAsync();

        var motorizzazioneTokens = TokenizeMotorizzazione(query.Motorizzazione);

        // Defect 1: the trim (motorizzazione) is matched token-by-token, not as
        // one literal substring. A mechanic's "3.0 multijet 180" must match the
        // stored "3.0 Multijet - 180 16v" despite the "- " separator and the
        // missing "16v" suffix - splitting the query into tokens (3.0 / multijet
        // / 180) and requiring each to appear in the stored trim does that, while
        // a distinct displacement/power token (150 vs 180) still keeps genuinely
        // different trims apart (verified in progress.md).
        var cars = await RunSearchAsync(conn, query, motorizzazioneTokens);

        // Defect 2: brand+model are in the catalogue but the given trim matched
        // nothing - do NOT dead-end to "Non abbiamo un {brand} {model}". Re-run
        // without the trim filter and return the model's available trims as a
        // car SELECTION (identification, never a document - Rule 1 holds). Only
        // fires when a trim was actually supplied and marca/modello can anchor
        // the broadened search, so a genuinely-absent car still returns nothing.
        if (cars.Count == 0 && motorizzazioneTokens is not null &&
            (query.Marca is not null || query.Modello is not null))
        {
            cars = await RunSearchAsync(conn, query, motorizzazioneTokens: null);
        }

        int? suggestedYearFrom = null;
        int? suggestedYearTo = null;
        // Only engage this fallback when the original request actually had
        // a year filter and still came back empty - a plain wrong
        // brand/model with no year filter at all should stay a simple
        // "not found" (count 0, no suggestion), not run an extra query for
        // no reason.
        if (cars.Count == 0 && (query.AnnoInizio.HasValue || query.AnnoFine.HasValue))
        {
            (suggestedYearFrom, suggestedYearTo) = await ComputeSuggestedYearRangeAsync(conn, query, motorizzazioneTokens);
        }

        return new VehicleResponse
        {
            Count = cars.Count,
            Cars = cars,
            SuggestedYearFrom = suggestedYearFrom,
            SuggestedYearTo = suggestedYearTo,
        };
    }

    private async Task<List<VehicleResult>> RunSearchAsync(
        NpgsqlConnection conn, VehicleQuery query, string[]? motorizzazioneTokens)
    {
        await using var cmd = new NpgsqlCommand($"""
            {SelectColumns}
            {CommonWhere}
              AND (@annoInizio::int IS NULL OR anno_fine_macchina IS NULL OR anno_fine_macchina >= @annoInizio)
              AND (@annoFine::int IS NULL OR anno_inizio_macchina IS NULL OR anno_inizio_macchina <= @annoFine)
            """, conn);
        BindCommonParams(cmd, query, motorizzazioneTokens);
        cmd.Parameters.AddWithValue("annoInizio", query.AnnoInizio ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("annoFine", query.AnnoFine ?? (object)DBNull.Value);
        return await ReadCarsAsync(cmd);
    }

    // marca/alimentazione are matched exactly (the caller passes the exact
    // brand/fuel text); modello is wildcard-wrapped because a mechanic says
    // "Daily" while gup_rows stores "Daily III" (real bug - see git history).
    // motorizzazione is passed as a token array and matched per-token in
    // CommonWhere. A null tokens array skips the trim filter entirely (defect 2
    // broadening). Tokens with ILIKE metachars (%,_) aren't a concern for this
    // domain - engine trims are alphanumerics + '.'/'-'/spaces only.
    private static void BindCommonParams(NpgsqlCommand cmd, VehicleQuery query, string[]? motorizzazioneTokens)
    {
        cmd.Parameters.AddWithValue("marca", query.Marca ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("modello", query.Modello is null ? (object)DBNull.Value : $"%{query.Modello}%");
        cmd.Parameters.AddWithValue("alimentazione", query.Alimentazione ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("motorizzazioneTokens", (object?)motorizzazioneTokens ?? DBNull.Value);
        cmd.Parameters.AddWithValue("codiceMotore", query.CodiceMotore ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("kw", query.Kw ?? (object)DBNull.Value);
    }

    // Splits a free-text trim into match tokens on whitespace, dropping pure
    // separators ("-") so only content tokens (3.0, multijet, 180, 16v) remain.
    // Returns null when there's nothing to match on, which callers treat as "no
    // trim filter".
    private static string[]? TokenizeMotorizzazione(string? motorizzazione)
    {
        if (string.IsNullOrWhiteSpace(motorizzazione)) return null;
        var tokens = motorizzazione
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Any(char.IsLetterOrDigit))
            .ToArray();
        return tokens.Length == 0 ? null : tokens;
    }

    // Same marca/modello/alimentazione/motorizzazione/codiceMotore/kw
    // filters as the main query, deliberately without the year constraint -
    // tells the caller whether the brand+model exist at all (count > 0)
    // and, if so, the real production year range to suggest, computed
    // directly from gup_rows rather than approximated. anno_fine_macchina
    // can be 9999 ("still in production, no end year yet") - returned as-is
    // here; turning that into a sensible "to today" phrase is Chat
    // Service's concern, not this query's.
    private static async Task<(int? From, int? To)> ComputeSuggestedYearRangeAsync(
        NpgsqlConnection conn, VehicleQuery query, string[]? motorizzazioneTokens)
    {
        await using var cmd = new NpgsqlCommand($"""
            SELECT MIN(anno_inizio_macchina), MAX(anno_fine_macchina), COUNT(*)
            FROM gup_rows
            {CommonWhere}
            """, conn);
        // Same token-based trim matching as the main query, so "we have this
        // model for years X-Y" isn't wrongly suppressed by a literal-substring
        // trim miss.
        BindCommonParams(cmd, query, motorizzazioneTokens);

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
