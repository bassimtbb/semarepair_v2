namespace SearchService.Models;

// Common shape for the three search entry points (fault-code, symptom, system).
// See docs/SemaRepair_Architecture.md section 6.4.
public class SearchRequest
{
    public string? FaultCode { get; set; }
    public string? Symptom { get; set; }
    public string? SystemName { get; set; }
    public string? EngineCode { get; set; }
    // Not in the doc's example URLs, but needed to resolve EngineCode to a
    // specific car - engine codes alone span multiple brands in real data
    // (e.g. 8140.43S appears on FIAT/CITROEN/PEUGEOT/IVECO). Without this,
    // Rule 8's cross-brand SHARES_ENGINE_WITH fallback would be unreachable.
    public string? Brand { get; set; }
    public string Language { get; set; } = "it";
}
