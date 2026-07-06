namespace SearchService.Models;

// Matches the response contract in docs/SemaRepair_Architecture.md section 6.4.
public class SearchResponse
{
    // "document" | "car_selection" | "not_found" | "vague" | "redirected"
    public string ResultType { get; set; } = "not_found";
    public int Count { get; set; }
    public bool SelectionNeeded { get; set; }
    public string? RedirectedTo { get; set; }
    public List<DocumentResult> Documents { get; set; } = [];
    public List<CarSummary> Cars { get; set; } = [];
    public string? ValidationMessage { get; set; }
}

public class DocumentResult
{
    public string IdDocumento { get; set; } = "";
    public string SiglaDocumento { get; set; } = "";
    public string Titolo { get; set; } = "";
    public string Impianto { get; set; } = "";
    public string Dispositivo { get; set; } = "";
    public string Anomalia { get; set; } = "";
    public string Causa { get; set; } = "";
    public string Intervento { get; set; } = "";
    public string Procedura { get; set; } = "";
    public string Nota { get; set; } = "";
    public int Reliability { get; set; }
    public string Language { get; set; } = "it";
    public List<FaultCodeInfo> DtcCodes { get; set; } = [];
    public bool FoundViaSharedEngine { get; set; }
    public string? SharedEngineInfo { get; set; }
    // Set when the vector search found nothing within MaxRelevantDistance
    // (see SearchController) - this is the nearest document available, not
    // a confirmed match for the symptom described. LowConfidenceReason is
    // the document's own real Impianto/Dispositivo, never invented text.
    public bool LowConfidenceMatch { get; set; }
    public string? LowConfidenceReason { get; set; }
}

// Description is the DTC code's own explanation, taken verbatim from this
// specific document's resx text (graph_edges.description on the
// CONTAINS_FAULT edge) - never invented, and never merged across
// documents that might phrase the same code slightly differently.
public class FaultCodeInfo
{
    public string Code { get; set; } = "";
    public string? Description { get; set; }
}

public class CarSummary
{
    public string IdMacchina { get; set; } = "";
    public string Marca { get; set; } = "";
    public string Modello { get; set; } = "";
    public string? Motorizzazione { get; set; }
    public string CodiceMotore { get; set; } = "";
    public string? Alimentazione { get; set; }
    public int? AnnoInizio { get; set; }
    public int? AnnoFine { get; set; }
    public int? Kw { get; set; }
    public int? Cavalli { get; set; }
}
