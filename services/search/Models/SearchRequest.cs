namespace SearchService.Models;

// Common shape for the three search entry points (fault-code, symptom, system).
// See docs/SemaRepair_Architecture.md section 6.4.
public class SearchRequest
{
    public string? FaultCode { get; set; }
    public string? Symptom { get; set; }
    public string? SystemName { get; set; }
    public string? EngineCode { get; set; }
    public string Language { get; set; } = "it";
}
