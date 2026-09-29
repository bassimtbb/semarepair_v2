namespace SearchService.Models;

// What the loaded archive actually contains - GET /api/search/coverage.
//
// Every number here is a SQL aggregate over the real tables, never a
// constant. The help drawer that displays them tells the client this is a
// demo carrying one vehicle, and states its size; a size written into the
// frontend goes stale the first time the archive grows. Five wiring
// diagrams are currently missing their drawing and have been asked for,
// so the day they arrive an ingestion run has to be enough to correct what
// the interface claims. Nobody should have to remember to edit a number in
// a TypeScript file.
public class CoverageResponse
{
    public List<CoverageVehicle> Vehicles { get; set; } = [];

    // Repair sheets (anomalia/causa/intervento), across all languages -
    // the other half of the archive, beside the technical chunks counted
    // per language below.
    public long RepairDocuments { get; set; }

    public List<CoverageLanguage> Languages { get; set; } = [];

    // Every DTC the archive can answer, sorted. Listed rather than
    // summarised because the failure it prevents is specific: a tester who
    // types a code at random gets a correct "not found" and concludes the
    // product is broken. The first client to try it did exactly that, with
    // P1200 - a code this archive does not carry - and reported back that
    // it was not working well.
    public List<string> FaultCodes { get; set; } = [];
}

public class CoverageVehicle
{
    public string? Marca { get; set; }
    public string? Modello { get; set; }
    public string? Motorizzazione { get; set; }
    public int? AnnoInizio { get; set; }
    public int? AnnoFine { get; set; }
    public string? Alimentazione { get; set; }
    public int? Kw { get; set; }
    public int? Cavalli { get; set; }
    public string? CodiceMotore { get; set; }
}

// One row per language present in knowledge_chunks. The counts differ
// sharply between them in the current archive - Italian carries the manual
// and the diagrams, French, Spanish and Portuguese carry neither - and the
// language menu shows that difference rather than hiding it. Offering a
// language whose archive is empty is a promise the data cannot keep.
public class CoverageLanguage
{
    public string Code { get; set; } = "";

    public long Facts { get; set; }
    public long Procedures { get; set; }
    public long ManualPages { get; set; }

    // Distinct drawings that can actually be DISPLAYED, not legend rows: a
    // legend whose asset_id is null is a diagram whose component list was
    // delivered without the drawing itself, and counting it would promise
    // a picture that does not exist. Counted per asset because one diagram
    // contributes one legend row per component - 43 rows for the injection
    // schematic alone.
    public long Diagrams { get; set; }
}
