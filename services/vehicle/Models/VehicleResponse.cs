namespace VehicleService.Models;

// See docs/SemaRepair_Architecture.md section 6.5.
//
// SuggestedYearFrom/SuggestedYearTo are populated only by
// VehicleSearchService.SearchAsync's fallback path: the original query
// had a year filter, came back empty, and the same marca/modello (+ other
// non-year filters) exist for *some* year range once that constraint is
// dropped. Null/null means either no year filter was given, the query
// already matched something, or the marca/modello don't exist at all -
// Chat Service uses null/null as the signal for "this model doesn't exist
// in our data," distinct from "it exists, just not for that year."
public class VehicleResponse
{
    public int Count { get; set; }
    public List<VehicleResult> Cars { get; set; } = [];
    public int? SuggestedYearFrom { get; set; }
    public int? SuggestedYearTo { get; set; }
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
