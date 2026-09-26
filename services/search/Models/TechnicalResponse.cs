namespace SearchService.Models;

// Response shape for GET /api/search/technical - the extension's own
// endpoint (docs/Architecture_Extension_v2.md). Deliberately separate from
// SearchResponse: a technical answer is not a repair document and forcing
// it into DocumentResult would mean a dozen always-null fields on both
// sides, plus a shared type that two features could fight over.
public class TechnicalResponse
{
    // "technical" | "not_found"
    public string ResultType { get; set; } = "not_found";
    public int Count { get; set; }
    public List<TechnicalChunk> Chunks { get; set; } = [];
}

public class TechnicalChunk
{
    public string IdDocumento { get; set; } = "";
    public string Language { get; set; } = "it";

    // 'fact' | 'legend' | 'section' - drives how the frontend renders this,
    // and it is the reason a single search tool can answer three different
    // kinds of question. See the routing section of the architecture doc.
    public string Kind { get; set; } = "";

    public string? Heading { get; set; }     // Gruppo, fuse-box, chapter title
    public string? Label { get; set; }       // Dato, Descrizione, NomeComp
    public string? Value { get; set; }       // the answer: 50 (A), 10, H7
    public string? Unit { get; set; }        // Nm, grammi, Volt / Watt - Tipo
    public string? Reference { get; set; }   // F04, H1, S1
    public string? Body { get; set; }        // prose, sections only

    // RifIDFilePDF, resolved against Data/PDF when serving. Non-null only
    // on legend chunks belonging to a wiring diagram.
    public string? AssetId { get; set; }

    // The parent document's own title ("Fusibili e Relè", "Airbag Siemens
    // MY99"). Carried so the frontend can always name the source - same
    // transparency rule as the repair cards.
    public string? DocumentTitle { get; set; }

    // Cosine distance, smaller is closer. Returned rather than hidden so a
    // bad answer can be diagnosed from the response alone, without
    // re-running the query against the database.
    public double Distance { get; set; }
}
