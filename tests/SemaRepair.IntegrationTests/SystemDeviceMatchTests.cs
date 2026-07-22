using System.Net;

namespace SemaRepair.IntegrationTests;

// L3: MatchSystemOrDeviceAsync used to narrow a no-car symptom search to a
// device's documents whenever the device NAME appeared as a substring of the
// symptom - even when it was mentioned only as a location ("...la spia sul
// quadro strumenti"), silently restricting an engine-fault search to
// instrument-cluster documents. The fix narrows only when the device is the
// grammatical SUBJECT (not governed by an Italian locative preposition).
//
// This guards BOTH directions - the whole point of the fix is to separate them:
//  - incidental locative mention must NOT narrow;
//  - genuine device-subject must STILL narrow.
//
// Surface: the "Symptom search narrowed to system/device 'X'" / "not narrowed"
// log line, which directly reports MatchSystemOrDeviceAsync's decision (the no-
// car endpoint returns only a car selection, so the narrowing isn't cleanly
// visible in the body). Assertions are query-specific so concurrent log noise
// can't make them pass or fail spuriously.
public class SystemDeviceMatchTests
{
    // Engine-performance fault; "sul quadro strumenti" only says WHERE the
    // warning light is - it must not narrow to the instrument cluster.
    private const string IncidentalQuery = "accensione spia avaria motore sul quadro strumenti";

    // The instrument cluster genuinely IS the subject ("al" is not locative) -
    // this must still narrow.
    private const string SubjectQuery = "problema al quadro strumenti";

    [Fact]
    public async Task IncidentalDeviceMention_DoesNotNarrow()
    {
        var since = TestEnv.NowUnix() - 2;
        var resp = await Search(IncidentalQuery);
        Assert.Equal(HttpStatusCode.OK, resp);

        await Task.Delay(500);
        var logs = TestEnv.SearchLogsSince(since);

        // NON-VACUITY: under the old substring behaviour this query narrowed to
        // 'Quadro strumenti' (verified in the red-proof, progress.md §28) - so
        // this negative assertion genuinely can fail.
        Assert.False(
            logs.Contains($"narrowed to system/device 'Quadro strumenti' (query: '{IncidentalQuery}')"),
            $"L3 regression: an incidental locative mention narrowed the search to the instrument cluster.\n{Tail(logs)}");

        // Positive confirmation it took the broad path for THIS query.
        Assert.Contains($"not narrowed - no system/device subject in '{IncidentalQuery}'", logs);
    }

    [Fact]
    public async Task GenuineDeviceSubject_StillNarrows()
    {
        var since = TestEnv.NowUnix() - 2;
        var resp = await Search(SubjectQuery);
        Assert.Equal(HttpStatusCode.OK, resp);

        await Task.Delay(500);
        var logs = TestEnv.SearchLogsSince(since);

        // The fix must not over-correct: a genuine device-subject symptom still
        // narrows to that device.
        Assert.Contains($"narrowed to system/device 'Quadro strumenti' (query: '{SubjectQuery}')", logs);
    }

    private static async Task<HttpStatusCode> Search(string q)
    {
        var resp = await TestEnv.Http.GetAsync($"/api/search/symptom?q={Uri.EscapeDataString(q)}&lang=it");
        return resp.StatusCode;
    }

    private static string Tail(string s)
    {
        var lines = s.Split('\n');
        return string.Join('\n', lines[^Math.Min(15, lines.Length)..]);
    }
}
