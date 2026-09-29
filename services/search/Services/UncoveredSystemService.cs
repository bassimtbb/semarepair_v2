using System.Text.RegularExpressions;
using Npgsql;

namespace SearchService.Services;

// "The vehicle has this system; none of its repair sheets covers it."
//
// Why a categorical check instead of a threshold. "Il fusibile dell'ABS si
// brucia sempre" scored 0.3443 against a sheet about a blown *injection*
// fuse, comfortably inside MaxRelevantDistance, and was presented as a
// confirmed match. Five of the query's six words describe that sheet
// exactly; the one word that makes it wrong - ABS - is diluted across 768
// dimensions. Nor can the cutoff be tightened to catch it: the calibration
// recorded in SearchController put a *correct* brake match at 0.337, seven
// thousandths below this wrong one. No cutoff separates them, so distance is
// the wrong instrument and this check does not use it.
//
// What it uses instead: the vehicle's own technical archive names its
// systems - a fuse labelled "Centralina ABS", a diagram legend "Centralina
// ABS+ASR". If the mechanic names one of those and not one of this vehicle's
// repair sheets mentions it, the closest sheet cannot be the answer, however
// close it scored. Wrong-but-confident is the one outcome the product may not
// produce: a mechanic who applies an injection procedure to a braking fault
// has been actively misled.
//
// Restricted to acronyms (ABS, ASR, EGR, DPF, SRS...) deliberately. They are
// this domain's system names, they are never ordinary words, and they are
// exactly where a single token flips a sentence the embedding otherwise reads
// as a match. Broadening it to every component noun was measured and
// rejected: the vocabulary grew to 178 tokens including "sistema", "vano" and
// "tipo", and "Fumo nero dallo scarico in accelerazione" tripped on
// "scarico" - a correct answer turned into a refusal. Narrowed to acronyms it
// is 6 tokens (ABS, ASR, CAN, LED, PTC, VIN) on the current data, and over 19
// real queries it rejected 4 wrong answers and no right ones.
//
// Both sides are read from the data, per vehicle, so there is no list to
// maintain here: a new vehicle arrives with its own acronyms and its own
// sheet coverage.
public class UncoveredSystemService
{
    private readonly string _connectionString;
    private readonly ILogger<UncoveredSystemService> _logger;

    // Length 3-6: two-letter runs are too ambiguous to act on ("DI" and "IL"
    // both appear capitalised in the archive and are just Italian words),
    // and nothing in the domain's vocabulary is longer.
    private static readonly Regex CandidateToken = new(@"\b[\p{L}]{3,6}\b", RegexOptions.Compiled);

    public UncoveredSystemService(IConfiguration configuration, ILogger<UncoveredSystemService> logger)
    {
        _connectionString = configuration["OUR_DB"] ?? "";
        _logger = logger;
    }

    /// <summary>
    /// Returns the acronym naming a system the vehicle has but no candidate
    /// repair sheet covers, or null when the symptom names no such system.
    /// A non-null result means the symptom must not be answered from these
    /// sheets at all.
    /// </summary>
    public async Task<string?> FindUncoveredSystemAsync(
        string symptom,
        IReadOnlyCollection<string> carIds,
        IReadOnlyCollection<string> candidateDocIds,
        string language)
    {
        if (carIds.Count == 0 || candidateDocIds.Count == 0) return null;

        // Tokenised here rather than in SQL so the query stays a lookup
        // against two small sets instead of a scan that re-tokenises every
        // document. Matching is case-insensitive on purpose: the archive
        // decides what counts as an acronym, not how the mechanic typed it.
        var queryTokens = CandidateToken.Matches(symptom)
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct()
            .ToArray();
        if (queryTokens.Length == 0) return null;

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            WITH named AS (
                -- Systems this vehicle's technical archive names, written as
                -- acronyms. Labels only: headings and procedure bodies contain
                -- ordinary Italian words shouted in capitals ("AGENDO",
                -- "PRIMA", "TUTTI"), which would make this fire on anything.
                SELECT DISTINCT lower(tok) AS tok
                FROM knowledge_chunks kc
                JOIN graph_edges ge
                  ON ge.to_id = kc.id_documento
                 AND ge.from_type = 'car' AND ge.from_id = ANY(@carIds)
                 AND ge.relation = 'DOCUMENTED_IN',
                     unnest(regexp_split_to_array(coalesce(kc.label, ''), '[^[:alnum:]]+')) AS tok
                WHERE kc.language = @lang AND tok ~ '^[A-Z]{3,6}$'
            ),
            covered AS (
                -- Every word of every candidate sheet, whole-word. Substring
                -- matching would report "abs" as covered by any word
                -- containing it, which is the trap AppearsAsSubject in
                -- GraphSearchService exists to avoid.
                SELECT DISTINCT lower(tok) AS tok
                FROM documents d,
                     unnest(regexp_split_to_array(
                         concat_ws(' ', d.titolo, d.impianto, d.dispositivo, d.anomalia,
                                        d.causa, d.intervento, d.procedura, d.nota),
                         '[^[:alnum:]]+')) AS tok
                WHERE d.language = @lang AND d.id_documento = ANY(@candidateIds)
            )
            SELECT n.tok FROM named n
            WHERE n.tok = ANY(@queryTokens)
              AND NOT EXISTS (SELECT 1 FROM covered c WHERE c.tok = n.tok)
            ORDER BY n.tok
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("carIds", carIds.ToArray());
        cmd.Parameters.AddWithValue("candidateIds", candidateDocIds.ToArray());
        cmd.Parameters.AddWithValue("queryTokens", queryTokens);
        cmd.Parameters.AddWithValue("lang", language);

        var result = await cmd.ExecuteScalarAsync();
        var uncovered = result as string;

        if (uncovered is not null)
            _logger.LogInformation(
                "Symptom names '{System}', which this vehicle has but no repair sheet covers - "
                + "refusing to answer from the {Count} candidate sheets (query: '{Symptom}')",
                uncovered.ToUpperInvariant(), candidateDocIds.Count, symptom);

        return uncovered;
    }
}
