using System.Net.Http.Json;
using System.Text.Json;

namespace SemaRepair.IntegrationTests;

// Two defects found from one real transcript: a mechanic asked "Il fusibile
// dell'ABS si brucia sempre" on a confirmed Ducato and got two injection
// repair sheets, presented as confirmed matches.
//
//  A - distance cannot reject a wrong answer whose wording is right. That
//      query scored 0.3443 against a blown-injection-fuse sheet, inside
//      MaxRelevantDistance. Tightening the cutoff cannot fix it: the
//      calibration recorded a CORRECT brake match at 0.337, seven
//      thousandths below. UncoveredSystemService rejects it categorically
//      instead - the vehicle has an ABS (two fuses in knowledge_chunks) and
//      not one of its repair sheets mentions it.
//
//  B - the symptom candidate set included every document type the resx
//      ingestion loads, not only repair sheets. A wiring diagram or a torque
//      table has a title and no anomalia/causa/intervento, so it could win a
//      symptom query on its title alone and render as a card with empty
//      fields. GetFaultDocumentsForCarsAsync now requires fault content.
//
// Both assert on the response body rather than on logs: the failure these
// pin is what the mechanic was shown, so that is what is checked.
//
// Vehicle-specific by nature - these depend on the FI0396 Ducato being the
// confirmed car and on its archive naming an ABS it has no sheet for. They
// skip rather than fail when that is not the data in the database.
public class UncoveredSystemTests
{
    private const string Engine = "8140.43S";
    private const string Marca = "FIAT";

    // --- A: a named system with no sheet is refused, not approximated ---

    [SkippableTheory]
    // The transcript's own query. Five of its six words describe the
    // injection-fuse sheet exactly; only "ABS" makes it wrong.
    [InlineData("Il fusibile dell'ABS si brucia sempre")]
    // Scored 0.3215 - further under the cutoff than the query above, so a
    // threshold change would have had to reject legitimate matches to catch
    // it.
    [InlineData("La spia ABS resta accesa")]
    // A second acronym from the same archive, proving the vocabulary is read
    // from the data rather than being a special case for "ABS".
    [InlineData("L'ASR interviene continuamente")]
    public async Task SymptomNamingASystemWithNoRepairSheet_IsRefused(string symptom)
    {
        Skip.IfNot(await ArchiveNamesAnUncoveredSystem(), "No uncovered system in this dataset.");

        var result = await Symptom(symptom);

        Assert.Equal("not_found", result.ResultType);
        Assert.Empty(result.Documents);
    }

    // The fix must not over-correct into a product that answers nothing. Each
    // of these scored between 0.21 and 0.34 and is a real answer; the first
    // deliberately keeps the shape of the failing query - a fuse that keeps
    // burning - with the one word that made it wrong removed.
    [SkippableTheory]
    [InlineData("Il fusibile si brucia sempre")]
    [InlineData("Il fusibile della centralina iniezione si brucia sempre")]
    [InlineData("Il motore non si avvia dopo un arresto in marcia")]
    [InlineData("L'airbag non funziona")]
    [InlineData("Spia avaria motore accesa e scarse prestazioni")]
    // "scarico" is named by the archive too. Broadening the guard beyond
    // acronyms made this query refuse; it is here so that regression cannot
    // return unnoticed.
    [InlineData("Fumo nero dallo scarico in accelerazione")]
    public async Task SymptomAboutACoveredSystem_StillAnswers(string symptom)
    {
        Skip.IfNot(TestEnv.DocumentsExist("199310118"), "FI0396 repair sheets absent.");

        var result = await Symptom(symptom);

        Assert.Equal("document", result.ResultType);
        Assert.NotEmpty(result.Documents);
    }

    // --- B: only documents that describe a fault can answer a symptom ---

    [SkippableFact]
    public async Task EveryDocumentReturnedForASymptom_CarriesFaultContent()
    {
        Skip.IfNot(TestEnv.DocumentsExist("199310118"), "FI0396 repair sheets absent.");

        // A spread of real symptoms rather than one, because the leak was
        // query-dependent: "Perdita di liquido refrigerante dal radiatore"
        // returned the LGR "Guide di Riparazione" entry at 0.3463 - under the
        // cutoff, so unflagged - purely on its title.
        string[] symptoms =
        [
            "Perdita di liquido refrigerante dal radiatore",
            "Il motore non si avvia dopo un arresto in marcia",
            "Il climatizzatore non raffredda",
            "Le luci di emergenza non lampeggiano",
            "Lo sterzo vibra a velocita elevata",
        ];

        foreach (var symptom in symptoms)
        {
            var result = await Symptom(symptom);
            foreach (var doc in result.Documents)
            {
                var hasFaultContent = !string.IsNullOrWhiteSpace(doc.Anomalia)
                                   || !string.IsNullOrWhiteSpace(doc.Causa)
                                   || !string.IsNullOrWhiteSpace(doc.Intervento);

                Assert.True(hasFaultContent,
                    $"Symptom search returned a document with no fault content for '{symptom}': "
                    + $"{doc.IdDocumento} ('{doc.Titolo}'). A mechanic sees a card with empty "
                    + "Anomalia/Causa/Intervento rows.");
            }
        }
    }

    // The technical documents excluded from symptom search must still be
    // reachable - they were not deleted, they belong to the other path. Pinned
    // because "fix the leak by dropping them" would pass every test above
    // while removing a feature.
    [SkippableFact]
    public async Task ExcludingThemFromSymptomSearch_DoesNotHideThemFromTechnicalSearch()
    {
        Skip.IfNot(TestEnv.DocumentsExist("199310118"), "FI0396 repair sheets absent.");

        var resp = await TestEnv.Http.GetAsync(
            "/api/search/technical?q=" + Uri.EscapeDataString("Quale fusibile protegge la centralina ABS")
            + $"&codiceMotore={Engine}&marca={Marca}&lang=it");
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("ABS", body, StringComparison.OrdinalIgnoreCase);
    }

    // --- helpers ---

    // The guard only has something to reject when the archive names a system
    // that no sheet of the same vehicle covers. Asking the database directly
    // keeps the tests honest against a reloaded or extended dataset.
    private static async Task<bool> ArchiveNamesAnUncoveredSystem()
    {
        await Task.CompletedTask;
        var count = TestEnv.Scalar("""
            WITH named AS (
                SELECT DISTINCT lower(tok) AS tok
                FROM knowledge_chunks,
                     unnest(regexp_split_to_array(coalesce(label, ''), '[^[:alnum:]]+')) AS tok
                WHERE language = 'it' AND tok ~ '^[A-Z]{3,6}$'
            ),
            covered AS (
                SELECT DISTINCT lower(tok) AS tok
                FROM documents d,
                     unnest(regexp_split_to_array(
                         concat_ws(' ', d.titolo, d.impianto, d.dispositivo, d.anomalia,
                                        d.causa, d.intervento, d.procedura, d.nota),
                         '[^[:alnum:]]+')) AS tok
                WHERE d.language = 'it'
                  AND (d.anomalia IS NOT NULL OR d.causa IS NOT NULL OR d.intervento IS NOT NULL)
            )
            SELECT count(*) FROM named n
            WHERE NOT EXISTS (SELECT 1 FROM covered c WHERE c.tok = n.tok)
            """);
        return int.TryParse(count.Trim(), out var n) && n > 0;
    }

    private static async Task<SymptomResult> Symptom(string q)
    {
        var resp = await TestEnv.Http.GetAsync(
            $"/api/search/symptom?q={Uri.EscapeDataString(q)}&codiceMotore={Engine}&marca={Marca}&lang=it");
        resp.EnsureSuccessStatusCode();

        return await resp.Content.ReadFromJsonAsync<SymptomResult>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"No body for '{q}'.");
    }

    private sealed record SymptomResult(string ResultType, List<Doc> Documents)
    {
        public List<Doc> Documents { get; init; } = Documents ?? [];
    }

    private sealed record Doc(
        string? IdDocumento, string? Titolo, string? Anomalia, string? Causa, string? Intervento);
}
