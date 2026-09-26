using System.Net;
using System.Text.Json;

namespace SemaRepair.IntegrationTests;

// Locks the behaviour of GET /api/search/technical - the extension's search
// path (docs/Architecture_Extension_v2.md).
//
// Why this file exists: MaxTechnicalDistance = 0.30 was not chosen, it was
// measured, over 34 real queries against the FI0396 data. That measurement
// lived in a throwaway script, which meant nobody could reproduce it and any
// later change to the parser, the embedded text or the threshold would have
// degraded answers silently. These tests are that measurement, made runnable.
//
// The margin is thin by nature: the worst true answer scored 0.288 and the
// best false one 0.305. So this is not a formality - it is the only thing
// standing between a tuned system and a plausible-sounding one.
//
// No Gemini chat turn is involved: the endpoint is hit directly, so the LLM's
// tool choice cannot mask or cause a failure here. Each query does cost one
// embedding call (fractions of a cent).
[Trait("Category", "Technical")]
public class TechnicalSearchTests
{
    private const string EngineCode = "8140.43S";
    private const string Brand = "FIAT";

    private static async Task<JsonElement> QueryAsync(
        string q, string lang = "it", string? engineCode = EngineCode, int limit = 5)
    {
        var url = $"/api/search/technical?q={Uri.EscapeDataString(q)}&lang={lang}&limit={limit}"
                + (engineCode is null ? "" : $"&codiceMotore={Uri.EscapeDataString(engineCode)}&marca={Brand}");
        var resp = await TestEnv.Http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static JsonElement[] Chunks(JsonElement root) =>
        root.GetProperty("chunks").EnumerateArray().ToArray();

    private static string? Str(JsonElement chunk, string name) =>
        chunk.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    // A question a mechanic actually asks, and the text that must appear in
    // the top answer. Deliberately asserts on content, not on distance: the
    // numbers will drift as the corpus grows, the answers must not.
    public static TheoryData<string, string> AnswerableQuestions => new()
    {
        { "quale fusibile protegge la centralina ABS", "ABS" },
        { "quale fusibile per le candelette",          "candelet" },
        { "coppia di serraggio del coperchio delle punterie", "punterie" },
        { "che lampadina per l'anabbagliante",         "Anabbagliante" },
        { "dove si trova la presa diagnosi",           "diagnosi" },
        { "come azzerare l'indicatore di assistenza",  "Sistema" },
        { "quantita di gas refrigerante",              "Gas Refrigerante" },
        { "cilindrata del motore",                     "Cilindrata" },
        { "pinout della presa diagnosi",               "PinOut" },
        { "ogni quanti km sostituire la cinghia",      "CINGHIE" },
    };

    [Theory]
    [MemberData(nameof(AnswerableQuestions))]
    public async Task TechnicalQuestion_IsAnswered(string question, string expectedInTopAnswer)
    {
        var root = await QueryAsync(question);
        Assert.Equal("technical", root.GetProperty("resultType").GetString());

        var top = Chunks(root).First();
        var haystack = string.Join(" | ", new[] { "heading", "label", "value", "reference", "body" }
            .Select(f => Str(top, f)).Where(s => s is not null));

        Assert.Contains(expectedInTopAnswer, haystack, StringComparison.OrdinalIgnoreCase);
    }

    // The other half of the threshold. Without these, raising
    // MaxTechnicalDistance to rescue one stubborn query would look free.
    [Theory]
    [InlineData("che tempo fa a Milano")]
    [InlineData("come si chiama il presidente della repubblica")]
    [InlineData("qual e la ricetta della carbonara")]
    [InlineData("pressione degli pneumatici")]       // plausible, simply absent
    [InlineData("codice PIN dell'autoradio")]        // plausible, simply absent
    public async Task OffTopicOrAbsent_ReturnsNothing(string question)
    {
        var root = await QueryAsync(question);
        Assert.Equal("not_found", root.GetProperty("resultType").GetString());
        Assert.Empty(Chunks(root));
    }

    // Same rule the document endpoints enforce (M1 / Rule 1). A torque figure
    // for the wrong engine is worse than no answer at all.
    [Fact]
    public async Task NoConfirmedVehicle_ReturnsNothing()
    {
        var root = await QueryAsync("quale fusibile protegge la centralina ABS", engineCode: null);
        Assert.Equal("not_found", root.GetProperty("resultType").GetString());
    }

    [Fact]
    public async Task UnknownVehicle_ReturnsNothing()
    {
        var root = await QueryAsync("quale fusibile protegge la centralina ABS", engineCode: "NO_SUCH_ENGINE");
        Assert.Equal("not_found", root.GetProperty("resultType").GetString());
    }

    // A wiring diagram's answer is the diagram, not one row of its legend.
    // Before collapsing, "schema elettrico airbag" returned eight rows from
    // the same drawing - three of them literally "Fusibile 7,5A" - burying
    // every other result.
    [Fact]
    public async Task SchematicQuery_ReturnsOneEntryPerDiagram_WithItsPdf()
    {
        var root = await QueryAsync("schema elettrico airbag", limit: 6);
        var chunks = Chunks(root);
        Assert.NotEmpty(chunks);

        var legendDocs = chunks.Where(c => Str(c, "kind") == "legend")
                               .Select(c => Str(c, "idDocumento"))
                               .ToArray();
        Assert.Equal(legendDocs.Length, legendDocs.Distinct().Count());

        var top = chunks.First();
        Assert.Equal("legend", Str(top, "kind"));
        Assert.Contains("Airbag", Str(top, "heading") ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrEmpty(Str(top, "assetId")),
            "A schematic result must carry the id of the PDF that shows it.");
    }

    // Second case from a demo transcript. Some legend entries name a part by
    // its rating rather than its function - "Fusibile 7,5A" appears 16 times
    // across 6 schematics - so they distinguish nothing and repeat once per
    // drawing. Asking for "lo schema elettrico del fusibile F17" returned the
    // correct rating followed by five identical rows from five unrelated
    // diagrams: noise dressed as five answers.
    //
    // The question is also ill-posed - there is no wiring diagram OF a fuse -
    // so the bar is not "answer it perfectly" but "do not pad one real answer
    // with repetition".
    [Fact]
    public async Task RepeatedLegendLabels_AreNotReturnedOncePerDiagram()
    {
        var root = await QueryAsync("schema elettrico fusibile F17", limit: 10);
        var chunks = Chunks(root);
        Assert.NotEmpty(chunks);

        var labels = chunks.Where(c => Str(c, "kind") == "legend")
                           .Select(c => Str(c, "label"))
                           .Where(l => !string.IsNullOrEmpty(l))
                           .ToArray();
        Assert.Equal(labels.Length, labels.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // The real answer still leads: F17 is in the fuse table, not on a diagram.
        var top = chunks.First();
        Assert.Equal("fact", Str(top, "kind"));
        Assert.Equal("F17", Str(top, "reference"));
    }

    // Caught by reading an actual demo transcript, not by a test: asking for
    // the airbag diagram returned it at 0.215 AND three unrelated schematics -
    // ABS, ABS+ASR, immobiliser - at 0.292-0.298, all under the 0.30
    // threshold. They scored close because every schematic's indexed text now
    // begins with "Schema Elettrico", so the words the question shares with
    // all of them outweighed the one word that tells them apart.
    //
    // A fixed threshold cannot separate "one right answer" from "several";
    // the relative window can. This asserts the outcome - ask for one
    // diagram, get one diagram - rather than the window's value, so the
    // constant can be re-tuned without rewriting the test.
    [Fact]
    public async Task AskingForOneDiagram_DoesNotReturnTheOtherSchematics()
    {
        var root = await QueryAsync("schema elettrico airbag", limit: 10);
        var legends = Chunks(root).Where(c => Str(c, "kind") == "legend").ToArray();

        Assert.NotEmpty(legends);
        Assert.All(legends, c =>
            Assert.Contains("Airbag", Str(c, "heading") ?? "", StringComparison.OrdinalIgnoreCase));
    }

    // Caught in English, where four content-free "7.5A fuse" legend rows
    // scored 0.268-0.277 and pushed the real answer (0.271) to third place,
    // while the Italian phrasing of the same question ranked it first. A
    // language-dependent ranking is not acceptable in a product that answers
    // in five, so within a tie band a stated value outranks a pointer to a
    // drawing.
    [Theory]
    [InlineData("it", "quale fusibile protegge la centralina ABS")]
    [InlineData("fr", "quel fusible protege le calculateur ABS")]
    [InlineData("en", "which fuse protects the ABS control unit")]
    [InlineData("es", "que fusible protege la centralita ABS")]
    [InlineData("pt", "que fusivel protege a centralina ABS")]
    public async Task SameFuseQuestion_AnswersWithAFact_InEveryLanguage(string lang, string question)
    {
        var root = await QueryAsync(question, lang);
        Assert.Equal("technical", root.GetProperty("resultType").GetString());

        var top = Chunks(root).First();
        Assert.Equal("fact", Str(top, "kind"));
        Assert.False(string.IsNullOrEmpty(Str(top, "value")),
            $"[{lang}] the top answer must state a rating, not merely point at a diagram.");
        Assert.Contains("ABS", $"{Str(top, "label")} {Str(top, "heading")}", StringComparison.OrdinalIgnoreCase);
    }
}
