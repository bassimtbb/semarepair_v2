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

public class CarOption
{
    public string IdMacchina { get; set; } = "";
    public string CodiceMotore { get; set; } = "";
    public int? AnnoInizio { get; set; }
    public int? AnnoFine { get; set; }
}

public class CaseSummary
{
    public string Sigla { get; set; } = "";
    public string Impianto { get; set; } = "";
    public string Dispositivo { get; set; } = "";
    public string Causa { get; set; } = "";
    public int Reliability { get; set; }
}
