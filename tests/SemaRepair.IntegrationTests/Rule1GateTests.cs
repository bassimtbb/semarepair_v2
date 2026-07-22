namespace SemaRepair.IntegrationTests;

// M1 / Rule 1: a repair document must NEVER be shown before a vehicle is
// confirmed. The dangerous case is an engine code typed in free text with no
// confirmed car - Gemini may extract it into the tool args, and before M1 that
// seeded the confirmed-car document search and leaked a document. The fix
// routes it to identification (Half A) and structurally gates document
// emission on a confirmed car (Half B).
//
// NON-VACUITY NOTE: the input must be one that WOULD leak a document if the
// gate were removed, otherwise the "zero cases" assertion passes trivially and
// proves nothing. "problemi iniezioni motore 8140.43S" is such an input -
// engine 8140.43S maps to 14 cars whose documents include injection faults, so
// with the M1 gate reverted this leaks 4 documents (verified in progress.md
// §25.3 and re-confirmed as the RED half of this test's red/green check in
// §27). "P0504 su motore F1AE0481C" was rejected as the primary input: that
// engine has no P0504 document, so it can never leak and the test would be
// vacuous.
public class Rule1GateTests
{
    [Fact]
    public async Task SymptomWithEngineCode_NoConfirmedCar_EmitsNoDocument()
    {
        var events = await ChatClient.SendAsync(
            "problemi iniezioni motore 8140.43S",
            ChatClient.FreshSession("rule1"),
            language: "it");

        Assert.NotEmpty(events);

        // The load-bearing Rule 1 assertion: not a single event may carry a
        // repair document. Absent the gate this input emits 4 documents, so a
        // failure here is a real Rule 1 violation, not a vacuous pass.
        foreach (var e in events)
        {
            Assert.True(e.CaseCount == 0,
                $"Rule 1 violation: a document ({e.CaseCount} case(s)) was emitted for an engine code with NO confirmed car (phase={e.Phase}).");
        }

        // It should have degraded to identification (a car selection), never a
        // 'found document' turn.
        Assert.DoesNotContain(events, e => e.Phase == "chat" && e.CaseCount > 0);
    }
}
