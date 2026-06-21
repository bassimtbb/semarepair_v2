namespace ChatService.Models;

// Streamed as SSE chunks. Shape follows the example in
// docs/EmbeddingAndGraph_Technical.md section 8 - first draft, to be
// refined when Chat Service is actually built (last in the roadmap order).
public class ChatResponse
{
    // "identification" | "chat"
    public string Phase { get; set; } = "chat";
    public bool Found { get; set; }
    public string? Message { get; set; }
    public List<CarOption> CarMatches { get; set; } = [];
    public List<CaseSummary> Cases { get; set; } = [];
}

// Spliced directly from Search/Vehicle Service's raw JSON in
// RepairOrchestrator, same as CaseSummary - applying the same fidelity
// reasoning consistently rather than trusting Gemini's JSON for cars but
// not documents.
public class CarOption
{
    public string IdMacchina { get; set; } = "";

    // Brand/model/trim - missing from the original first draft, but the
    // mechanic can't tell cards apart without them (e.g. distinguishing a
    // FIAT Ducato from a FORD Focus that happens to share an engine code).
    public string Marca { get; set; } = "";
    public string Modello { get; set; } = "";
    public string? Motorizzazione { get; set; }

    public string CodiceMotore { get; set; } = "";
    public int? AnnoInizio { get; set; }
    public int? AnnoFine { get; set; }
}

// All fields here are spliced directly from Search Service's raw
// DocumentResult JSON in RepairOrchestrator, never from Gemini's JSON
// output - the repair content (intervento above all) is what the mechanic
// actually needs, and an LLM reproducing it verbatim isn't a guarantee.
public class CaseSummary
{
    public string IdDocumento { get; set; } = "";
    public string Sigla { get; set; } = "";
    public string Impianto { get; set; } = "";
    public string Dispositivo { get; set; } = "";
    public string Anomalia { get; set; } = "";
    public string Causa { get; set; } = "";
    public string Intervento { get; set; } = "";
    public string Procedura { get; set; } = "";
    public string Nota { get; set; } = "";
    public int Reliability { get; set; }
    public string Language { get; set; } = "";
    public List<string> DtcCodes { get; set; } = [];
}
