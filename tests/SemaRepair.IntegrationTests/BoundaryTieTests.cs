using System.Net;
using System.Text.Json;

namespace SemaRepair.IntegrationTests;

// §5 boundary-tie regression. Two documents (199309673 / 199309676) carry
// byte-identical anomalia text, so their embeddings are identical and they
// always tie. progress.md §21 documents a query that lands them at ranks 5-6 -
// straddling the LIMIT=5 cutoff - so a plain `LIMIT 5` would silently drop one.
// The fix fetches limit+1, detects the boundary tie, and re-queries with an
// epsilon filter so both are returned.
//
// Surface: the no-car symptom endpoint returns only a car selection (Rule 1
// hides document identity), and the one tied pair that falls inside the tie
// window shares all its cars - so "both documents returned" is NOT visible in
// the HTTP body (see progress.md §27 for the full analysis). Instead this
// asserts the fix's faithful signature: the "Boundary tie detected" re-query
// log line, emitted ONLY by the epsilon re-query. Remove the fix and the line
// disappears (proven red in §27).
public class BoundaryTieTests
{
    // The documented §21 boundary query - positions 199309673/199309676 at
    // ranks 5-6, inside the 0.02 tie window. Contains no system/device name, so
    // the endpoint takes the full-table Type-4 path (not a narrowed search).
    private const string BoundaryQuery =
        "Notevole calo di prestazioni e potenza con accensione spia avaria motore sul cruscotto";

    [Fact]
    public async Task BoundaryTie_ReQuery_FiresForStraddlingTie()
    {
        var since = TestEnv.NowUnix() - 2;

        var url = $"/api/search/symptom?q={Uri.EscapeDataString(BoundaryQuery)}&lang=it";
        var resp = await TestEnv.Http.GetAsync(url);
        var body = await resp.Content.ReadAsStringAsync();

        // Sanity: the query is processed as a no-car Type-4 symptom search.
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(body);
        var resultType = doc.RootElement.GetProperty("resultType").GetString();
        Assert.Equal("car_selection", resultType);

        // The load-bearing assertion: the boundary-tie re-query fired. This line
        // is logged only from the epsilon re-query in SymptomSearchService - if
        // the fix were reverted to a plain LIMIT, the straddling tie would be
        // silently truncated and this line would never appear.
        await Task.Delay(500); // let the log line flush
        var logs = TestEnv.SearchLogsSince(since);
        Assert.True(
            logs.Contains("Boundary tie detected"),
            $"Expected a 'Boundary tie detected' re-query log line for the boundary query, but none was found in search-service logs since {since}. " +
            $"This means the straddling tie was silently truncated (the §5 bug). Log tail:\n{Tail(logs)}");
    }

    private static string Tail(string s)
    {
        var lines = s.Split('\n');
        return string.Join('\n', lines[^Math.Min(15, lines.Length)..]);
    }
}
