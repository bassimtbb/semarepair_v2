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
            WHERE (@brand::text IS NULL OR marca_macchina ILIKE @brand)
              AND (@model::text IS NULL OR modello_macchina ILIKE @model)
              AND (@fuel::text IS NULL OR alimentazione_macchina ILIKE @fuel)
              AND (@engineCode::text IS NULL OR codice_motore_macchina = @engineCode)
              AND (@kw::int IS NULL OR kw_macchina = @kw)
              AND (@yearFrom::int IS NULL OR anno_fine_macchina IS NULL OR anno_fine_macchina >= @yearFrom)
              AND (@yearTo::int IS NULL OR anno_inizio_macchina IS NULL OR anno_inizio_macchina <= @yearTo)
            """, conn);
        cmd.Parameters.AddWithValue("brand", query.Brand ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("model", query.Model ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("fuel", query.Fuel ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("engineCode", query.EngineCode ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("kw", query.Kw ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("yearFrom", query.YearFrom ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("yearTo", query.YearTo ?? (object)DBNull.Value);

        var cars = await ReadCarsAsync(cmd);
        return new VehicleResponse { Count = cars.Count, Cars = cars };
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
