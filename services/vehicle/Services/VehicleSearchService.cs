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

    public Task<VehicleResponse> SearchAsync(VehicleQuery query) =>
        throw new NotImplementedException();

    public Task<VehicleResult?> GetByIdAsync(string idMacchina) =>
        throw new NotImplementedException();
}
