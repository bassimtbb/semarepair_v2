namespace VehicleService.Models;

// GET /api/vehicles query params. Deliberate deviation from
// docs/SemaRepair_Architecture.md section 6.5, which specifies English
// query params (brand/model/yearFrom/yearTo/fuel/engineCode) alongside
// Italian response fields - standardized to match VehicleResult's
// existing Italian convention instead. See progress.md's decisions log.
//
// AnnoInizio/AnnoFine here are the filter range bounds, not a specific
// car's own production range - they're named to match VehicleResult's
// fields of the same name because they're compared directly against them
// under the overlap semantics in VehicleSearchService.SearchAsync
// (a car matches if its own AnnoInizio/AnnoFine range overlaps this
// query range at all).
//
// Motorizzazione vs CodiceMotore: Motorizzazione is the human-readable
// engine label a mechanic actually says out loud ("1.5 TDCi 8v"),
// matched with ILIKE. CodiceMotore is gup_rows' internal short code
// ("XVJB") - a mechanic essentially never knows or states this from
// memory, it's only ever echoed back after a car is already identified.
// A real bug came from conflating the two: Chat Service's FindCar tool
// had a single "engineCode" parameter, so Gemini put a mechanic's
// "1.5 TDCi 8v" straight into the exact-match CodiceMotore filter,
// silently matching zero rows instead of the 6 real ones.
public class VehicleQuery
{
    public string? Marca { get; set; }
    public string? Modello { get; set; }
    public int? AnnoInizio { get; set; }
    public int? AnnoFine { get; set; }
    public string? Alimentazione { get; set; }
    public string? Motorizzazione { get; set; }
    public string? CodiceMotore { get; set; }
    public int? Kw { get; set; }
}
