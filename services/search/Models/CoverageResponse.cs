namespace SearchService.Models;

// High-level archive inventory returned by GET /api/search/coverage.
public class CoverageResponse
{
    public long Vehicles { get; set; }
    public long RepairDocuments { get; set; }
    public long TechnicalDocuments { get; set; }
    public long TechnicalEntries { get; set; }
    public List<string> Languages { get; set; } = [];
}
