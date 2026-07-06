namespace SearchService.Models;

// Common shape for the three search entry points (fault-code, symptom, system).
// Deliberate deviation from docs/SemaRepair_Architecture.md section 6.4
// (English query params) - standardized to Italian to match Vehicle
// Service's completed convention. See progress.md's decisions log.
//
// Note: this model isn't actually bound via [FromQuery] anywhere -
// SearchController takes individual [FromQuery] parameters directly
// (marca/codiceMotore, matching the rename below) rather than binding to
// this class. Kept in sync anyway since it documents the shared shape.
public class SearchRequest
{
    public string? FaultCode { get; set; }
    public string? Symptom { get; set; }
    public string? SystemName { get; set; }
    public string? CodiceMotore { get; set; }
    // Not in the doc's example URLs, but needed to resolve CodiceMotore to
    // a specific car - engine codes alone span multiple brands in real
    // data (e.g. 8140.43S appears on FIAT/CITROEN/PEUGEOT/IVECO). Without
    // this, Rule 8's cross-brand SHARES_ENGINE_WITH fallback would be
    // unreachable.
    public string? Marca { get; set; }
    public string Language { get; set; } = "it";
}
