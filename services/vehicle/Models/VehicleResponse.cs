namespace VehicleService.Models;

// See docs/SemaRepair_Architecture.md section 6.5.
public class VehicleResponse
{
    public int Count { get; set; }
    public List<VehicleResult> Cars { get; set; } = [];
}

public class VehicleResult
{
    public string IdMacchina { get; set; } = "";
    public string Marca { get; set; } = "";
    public string Modello { get; set; } = "";
    public string? Motorizzazione { get; set; }
    public string CodiceMotore { get; set; } = "";
    public string? Alimentazione { get; set; }
    public int? AnnoInizio { get; set; }
    public int? AnnoFine { get; set; }
    public int? Kw { get; set; }
    public int? Cavalli { get; set; }
}
