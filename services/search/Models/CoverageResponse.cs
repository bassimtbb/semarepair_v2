namespace SearchService.Models;

// What the loaded archive actually contains - GET /api/search/coverage.
//
// Every number and every suggested question here is read from the real
// tables, never written by hand. The help drawer that displays them is a
// promise to the mechanic, and a promise compiled into the frontend goes
// stale the first time a car arrives: the archive went from one vehicle to
// four in an afternoon, and nothing in this response had to be edited.
public class CoverageResponse
{
    public List<CoverageVehicle> Vehicles { get; set; } = [];
    public List<CoverageLanguage> Languages { get; set; } = [];
}

public class CoverageVehicle
{
    public string IdMacchina { get; set; } = "";
    public string? Marca { get; set; }
    public string? Modello { get; set; }
    public string? Motorizzazione { get; set; }
    public int? AnnoInizio { get; set; }
    public int? AnnoFine { get; set; }
    public string? Alimentazione { get; set; }
    public int? Kw { get; set; }
    public int? Cavalli { get; set; }
    public string? CodiceMotore { get; set; }

    // What the mechanic types to select this car - brand, model and trim,
    // no years or fuel. Measured: it returns exactly one match per vehicle,
    // so the suggestion leads straight to a single card rather than a list.
    public string Query { get; set; } = "";

    public CoverageSections Sections { get; set; } = new();
}

// One block per kind of answer the product can give. A block the vehicle
// cannot fill comes back EMPTY, and the drawer then shows no section at all
// - the BMW carries no fault code, and saying nothing about codes is more
// honest than an empty list under a heading that promises some.
//
// The questions are the archive's own words - anomalia texts, chunk
// headings, the codes themselves. Nothing is phrased here: it is Italian
// workshop language, which is what the mechanic will type anyway, and the
// fifth vehicle will bring its own without a line being written.
public class CoverageSections
{
    // The product's core: a symptom in, a repair sheet out.
    public CoverageSection Cases { get; set; } = new();

    // Questions whose answer carries a picture that was REALLY delivered -
    // not merely referenced. See SEMAREPAIR_ASSETS_PATH.
    public CoverageSection Photos { get; set; } = new();

    // This vehicle's own DTCs, not the archive's.
    public CoverageSection FaultCodes { get; set; } = new();

    public CoverageSection Diagrams { get; set; } = new();
    public CoverageSection Manual { get; set; } = new();

    // Values with no picture attached - torques, pressures, thicknesses.
    public CoverageSection Technical { get; set; } = new();
}

public class CoverageSection
{
    // Everything the vehicle holds of this kind, which is what the counts
    // line states. Examples is a sample of it, not the whole.
    public long Total { get; set; }
    public List<string> Examples { get; set; } = [];
}

// One row per language present in knowledge_chunks. The counts differ
// sharply between them - Italian carries the manual and the diagrams,
// French, Spanish and Portuguese carry neither - and the language menu
// shows that difference rather than hiding it. Offering a language whose
// archive is empty is a promise the data cannot keep.
public class CoverageLanguage
{
    public string Code { get; set; } = "";
    public long Facts { get; set; }
    public long Procedures { get; set; }
    public long ManualPages { get; set; }

    // Distinct drawings that can be DISPLAYED: a legend whose asset is
    // missing is a diagram whose component list arrived without the
    // drawing, and counting it would promise a picture that does not exist.
    public long Diagrams { get; set; }
}
