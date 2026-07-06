namespace SearchService.Models;

// Aggregation DTOs for GET /api/usage/* - see docs/log-dashboard.md section
// 5. All numbers here are computed directly from gemini_usage_log by SQL
// aggregates (SUM/COUNT/date_trunc), never recomputed or estimated in this
// layer - same fidelity rule as the rest of this build.
public class UsageSummaryResponse
{
    public decimal TotalCostUsd { get; set; }
    public long TotalTokens { get; set; }
    public long TotalCalls { get; set; }
    public List<UsageBreakdown> ByModel { get; set; } = [];
    public List<UsageBreakdown> ByService { get; set; } = [];
}

public class UsageBreakdown
{
    public string Key { get; set; } = "";
    public decimal CostUsd { get; set; }
    public long Tokens { get; set; }
    public long Calls { get; set; }
}

public class UsageTimeSeriesPoint
{
    public DateTimeOffset BucketStart { get; set; }
    public decimal CostUsd { get; set; }
    public long Tokens { get; set; }
    public long Calls { get; set; }
}

public class UsageLogPage
{
    public List<UsageLogEntry> Entries { get; set; } = [];
    public bool HasMore { get; set; }
}

public class UsageLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string ServiceName { get; set; } = "";
    public string? Operation { get; set; }
    public string Model { get; set; } = "";
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public bool IsEstimated { get; set; }
    public decimal? CostUsd { get; set; }
    public string? SessionId { get; set; }
}
