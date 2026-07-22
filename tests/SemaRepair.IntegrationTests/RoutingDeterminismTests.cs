namespace SemaRepair.IntegrationTests;

// Routing determinism: "Problemi iniezioni" must route to SearchBySymptom
// EVERY time. This is the test a human can't cheaply re-verify by hand - it
// only means something across repetition (the routing call runs at temperature
// 0 + thinkingBudget 0 precisely so the tool choice never varies). A single run
// proves nothing; the value is in N runs all agreeing.
//
// Faithful surface: which search endpoint the turn hits. SearchBySymptom ->
// GET /api/search/symptom; SearchBySystem -> /api/search/system. We assert,
// per run, that the symptom endpoint was hit and the system endpoint was not -
// a direct echo of Gemini's actual tool choice, read from search-service logs.
//
// Tagged Slow: it makes 5 live Gemini-backed turns, so it's a separate/slower
// category (run all: `dotnet test`; skip slow: `dotnet test --filter Category!=Slow`).
[Trait("Category", "Slow")]
public class RoutingDeterminismTests
{
    private const int Runs = 5;

    [Fact]
    public async Task ProblemiIniezioni_RoutesToSearchBySymptom_EveryTime()
    {
        var routedToSymptom = 0;
        var failures = new List<string>();

        for (var i = 1; i <= Runs; i++)
        {
            var since = TestEnv.NowUnix() - 2;
            var events = await ChatClient.SendAsync("Problemi iniezioni", ChatClient.FreshSession($"routing-{i}"), "it");
            Assert.NotEmpty(events);

            await Task.Delay(400); // let the search-service request log flush
            var logs = TestEnv.SearchLogsSince(since);
            var hitSymptom = logs.Contains("/api/search/symptom");
            var hitSystem = logs.Contains("/api/search/system");

            if (hitSymptom && !hitSystem) routedToSymptom++;
            else failures.Add($"run {i}: symptomEndpoint={hitSymptom} systemEndpoint={hitSystem}");
        }

        Assert.True(routedToSymptom == Runs,
            $"Routing was not deterministic: only {routedToSymptom}/{Runs} runs routed to SearchBySymptom. Non-symptom runs: {string.Join("; ", failures)}");
    }
}
