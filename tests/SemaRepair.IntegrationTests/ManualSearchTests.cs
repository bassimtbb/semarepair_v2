using System.Net.Http.Json;
using System.Text.Json;

namespace SemaRepair.IntegrationTests;

// The scanned workshop manual, and the one way it can hurt the product.
//
// Indexing it was a clear win - 89 pages carrying the component photographs
// the structured archive never received. But a manual page is 1270 characters
// spanning photographs, a fuse list and two paragraphs of prose, where a fact
// is one labelled line. Its vector therefore sits at a middling distance from
// a great many questions, and under the threshold calibrated for short chunks
// it won by spreading rather than by answering: "mostrami lo schema elettrico
// della lavatrice" returned the fuse-box page at 0.276. A washing machine,
// answered from a Fiat 500 manual.
//
// MaxManualDistance exists for that, and these tests hold both ends of it:
// the page must still appear where it genuinely helps, and must not appear
// for a subject this vehicle has nothing to do with.
//
// Vehicle-specific by nature. They skip rather than fail when the Fiat 500
// corpus is not the one loaded.
public class ManualSearchTests
{
    private const string EngineCode = "169 A 4000";
    private const string Brand = "FIAT";

    // The manual is document 122, written by manual_seeder.py.
    private static bool ManualLoaded => TestEnv.ChunksExist("122");

    // --- it must not answer questions about other machines ---

    [SkippableTheory]
    // The case that started this, measured at 0.276 - the closest false
    // positive of the twenty queries used to calibrate the threshold.
    [InlineData("Mostrami lo schema elettrico della lavatrice")]
    [InlineData("Schema elettrico di un trattore")]        // 0.294
    [InlineData("Schema elettrico della lavastoviglie")]   // 0.295
    public async Task AQuestionAboutAnotherMachine_DoesNotReturnAManualPage(string question)
    {
        Skip.IfNot(ManualLoaded, "Fiat 500 manual not loaded.");

        var chunks = await QueryAsync(question);

        Assert.DoesNotContain(chunks, c => c.Kind == "manual");
    }

    // --- and it must still answer what it is there for ---

    [SkippableTheory]
    // Each of these was measured between 0.204 and 0.261, and each is a
    // subject the structured archive covers thinly or not at all - which is
    // why the manual was worth indexing in the first place.
    [InlineData("Dove si trova la scatola dei fusibili nel vano motore")]
    [InlineData("Dove si trova la presa diagnosi OBD")]
    [InlineData("Interruttore inerziale di sicurezza")]
    [InlineData("Pin out del connettore diagnostico")]
    public async Task AQuestionTheManualCovers_StillReturnsItsPage(string question)
    {
        Skip.IfNot(ManualLoaded, "Fiat 500 manual not loaded.");

        var chunks = await QueryAsync(question);

        Assert.Contains(chunks, c => c.Kind == "manual");
    }

    // Non-vacuity for the threshold: it must be a cut, not a wall. If a future
    // change rejected every manual page, the test above would still pass on
    // "fuse box" alone while the feature was dead - this fails loudly instead.
    [SkippableFact]
    public async Task TheThresholdRejectsSome_AndKeepsOthers()
    {
        Skip.IfNot(ManualLoaded, "Fiat 500 manual not loaded.");

        var kept = await QueryAsync("Dove si trova la scatola dei fusibili nel vano motore");
        var page = Assert.Single(kept.Where(c => c.Kind == "manual"));

        Assert.NotNull(page.AssetId);
        Assert.StartsWith("122", page.AssetId);
    }

    // --- a scanned page must never outrank an archive answer ---

    [SkippableFact]
    public async Task AStructuredFactComesFirst_EvenWhenThePageIsCloser()
    {
        Skip.IfNot(ManualLoaded, "Fiat 500 manual not loaded.");

        // Measured: the manual page sits at 0.241 here, nearer than any of the
        // three fuses (0.252, 0.257, 0.263). Distance alone would lead with a
        // photograph of a magazine; what the mechanic asked for is the rating,
        // and the archive has it exactly.
        var chunks = await QueryAsync("Quali fusibili protegge la centralina iniezione");

        Assert.NotEmpty(chunks);
        Assert.Equal("fact", chunks[0].Kind);
    }

    private static async Task<List<Chunk>> QueryAsync(string q)
    {
        var url = $"/api/search/technical?q={Uri.EscapeDataString(q)}"
                + $"&codiceMotore={Uri.EscapeDataString(EngineCode)}&marca={Brand}&lang=it&limit=8";
        var resp = await TestEnv.Http.GetAsync(url);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<Response>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return body?.Chunks ?? [];
    }

    private sealed record Response(List<Chunk> Chunks);
    private sealed record Chunk(string Kind, double Distance, string? Heading, string? AssetId);
}
