namespace VehicleService.Models;

// GET /api/vehicles query params. See docs/SemaRepair_Architecture.md section 6.5.
public class VehicleQuery
{
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public string? Fuel { get; set; }
    public string? EngineCode { get; set; }
    public int? Kw { get; set; }
}
