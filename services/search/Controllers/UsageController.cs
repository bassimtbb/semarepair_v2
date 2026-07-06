using Microsoft.AspNetCore.Mvc;
using SearchService.Models;
using SearchService.Services;

namespace SearchService.Controllers;

// Read-only aggregation over gemini_usage_log for the usage dashboard -
// see docs/log-dashboard.md section 5. Writes to that table happen from
// chat-service/search-service/ingestion-resx independently (each only
// ever writing its own rows); this controller is the one place reads are
// centralized.
[ApiController]
[Route("api/usage")]
public class UsageController : ControllerBase
{
    private static readonly string[] AllowedBuckets = ["hour", "day"];
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly UsageQueryService _usage;

    public UsageController(UsageQueryService usage)
    {
        _usage = usage;
    }

    [HttpGet("summary")]
    public Task<UsageSummaryResponse> Summary([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to) =>
        _usage.GetSummaryAsync(from, to);

    [HttpGet("timeseries")]
    public async Task<ActionResult<List<UsageTimeSeriesPoint>>> TimeSeries(
        [FromQuery] string bucket = "day", [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null)
    {
        if (!AllowedBuckets.Contains(bucket))
            return BadRequest($"bucket must be one of: {string.Join(", ", AllowedBuckets)}");
        return await _usage.GetTimeSeriesAsync(bucket, from, to);
    }

    [HttpGet("logs")]
    public Task<UsageLogPage> Logs(
        [FromQuery] string? service,
        [FromQuery] string? model,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 0,
        [FromQuery] int pageSize = DefaultPageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 0);
        return _usage.GetLogsAsync(service, model, from, to, page, pageSize);
    }
}
