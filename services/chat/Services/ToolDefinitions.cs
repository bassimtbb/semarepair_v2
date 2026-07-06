using System.Text.Json.Nodes;
using ChatService.Models;

namespace ChatService.Services;

// The 4 Gemini function declarations Chat Service exposes - 3 from v1
// (docs/SemaRepair_Architecture.md section 2.4: FindCar, SearchByFaultCode,
// SearchBySymptom) plus SearchBySystem, added because Search Service
// already has a tested /api/search/system endpoint and leaving it out of
// Gemini's tool list would be an arbitrary gap, not a deliberate omission.
//
// Parameter names match the doc's own tool-call examples where given
// (faultCode, engineCode, symptom - section 5.10 Rule 7/11), so the doc's
// existing worked examples still apply verbatim. "lang" is deliberately
// NOT a parameter here: it comes from the mechanic's session/request
// language, never something Gemini should infer from conversation text -
// RepairOrchestrator injects it directly when executing the HTTP call.
public static class ToolDefinitions
{
    public static GeminiFunctionDeclaration FindCar { get; } = new()
    {
        Name = "FindCar",
        Description =
            "Finds vehicles matching the given criteria. Use when the mechanic describes " +
            "their vehicle (brand, model, year, fuel type, engine - descriptive label or " +
            "internal code) and no car is confirmed yet for this session (Rule 7: if a " +
            "symptom is mentioned in the same message, identify the car first and search for " +
            "the symptom only after the mechanic confirms which car).",
        Parameters = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["brand"] = StringParam("Vehicle brand/manufacturer, e.g. FIAT, FORD, CITROEN."),
                ["model"] = StringParam("Vehicle model, e.g. Ducato, Focus."),
                ["yearFrom"] = IntParam("Earliest production year to match, if mentioned."),
                ["yearTo"] = IntParam("Latest production year to match, if mentioned."),
                ["fuel"] = StringParam(
                    "Fuel type, translated into exactly one of these 4 fixed database values " +
                    "(never the mechanic's own language/spelling): \"Diesel\", \"Benzina\", " +
                    "\"Gas\", \"Benzina/Elettrico\". E.g. Spanish \"diésel\"/\"gasolina\" -> " +
                    "\"Diesel\"/\"Benzina\"; French \"essence\" -> \"Benzina\"; English " +
                    "\"petrol\"/\"gas\" -> \"Benzina\". Unlike symptom/system text, this is a " +
                    "structured filter value, not free text - it must match the database's " +
                    "fixed vocabulary exactly, regardless of what language the mechanic used."),
                ["motorizzazione"] = StringParam(
                    "The engine as the mechanic actually describes it, e.g. \"1.5 TDCi 8v\", " +
                    "\"2.0 HDi\". This is what mechanics normally say - use this field for it."),
                ["engineCode"] = StringParam(
                    "An internal engine code, a short alphanumeric string like \"XVJB\" or " +
                    "\"F1AE0481C\" - NOT a descriptive label like \"1.5 TDCi 8v\" (that goes in " +
                    "motorizzazione instead). Mechanics essentially never know this from memory; " +
                    "only use it if they read one out from a document or a previous result."),
                ["kw"] = IntParam("Engine power in kW, if mentioned."),
            },
        },
    };

    public static GeminiFunctionDeclaration SearchByFaultCode { get; } = new()
    {
        Name = "SearchByFaultCode",
        Description =
            "Searches for repair documents matching a diagnostic trouble code (DTC) such " +
            "as P0504, C1215, B1024, U1600. Use whenever the mechanic provides a code " +
            "matching this pattern, even if they also describe a symptom in the same message.",
        Parameters = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["faultCode"] = StringParam("The DTC code exactly as written, e.g. P0504."),
                ["engineCode"] = StringParam("The session's confirmed engine code, if a car has been confirmed."),
                ["brand"] = StringParam("The session's confirmed vehicle brand, if known - engine codes are not unique across brands."),
            },
            ["required"] = new JsonArray("faultCode"),
        },
    };

    public static GeminiFunctionDeclaration SearchBySymptom { get; } = new()
    {
        Name = "SearchBySymptom",
        Description =
            "Searches for repair documents matching a described fault/symptom. The " +
            "\"symptom\" parameter must contain ONLY words the mechanic actually wrote, " +
            "after removing pure filler phrases - see the system instructions for the exact " +
            "cleaning rules and worked examples. Never translate, paraphrase, or invent " +
            "technical terms not present in the mechanic's own message.",
        Parameters = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["symptom"] = StringParam("The cleaned symptom description, in the mechanic's own language and exact words."),
                ["engineCode"] = StringParam("The session's confirmed engine code, if a car has been confirmed."),
                ["brand"] = StringParam("The session's confirmed vehicle brand, if known."),
                ["confirmLowConfidenceMatch"] = BoolParam(
                    "Set to true ONLY when the previous assistant turn asked whether the " +
                    "mechanic wants to see a low-confidence/uncertain match, and this message " +
                    "is a clear affirmative reply (e.g. \"sì\", \"yes\", \"ok\", \"mostramelo\", " +
                    "\"show me\"). Re-issue the exact same symptom text as the search that " +
                    "produced that offer. Omit or leave false otherwise."),
                ["secondarySymptom"] = StringParam(
                    "ONLY set when the mechanic's message describes two genuinely distinct, " +
                    "separate faults (not two variants of the same problem) - see the symptom " +
                    "cleaning rules. \"symptom\" gets the more specific of the two; this field " +
                    "gets the other one, in the mechanic's own exact words, never invented or " +
                    "paraphrased. If the first search finds nothing, this second symptom will " +
                    "be searched automatically - it is never silently discarded. Omit when only " +
                    "one symptom was described."),
            },
            ["required"] = new JsonArray("symptom"),
        },
    };

    public static GeminiFunctionDeclaration SearchBySystem { get; } = new()
    {
        Name = "SearchBySystem",
        Description =
            "Searches for repair documents by system or device name (e.g. \"Iniezione\", " +
            "\"Freni\", \"Candelette\") rather than a symptom description or fault code. Use " +
            "when the mechanic names a specific system/component directly without " +
            "describing a fault, or asks generically about a known system.",
        Parameters = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["systemName"] = StringParam("The system or device name, in the mechanic's own words."),
                ["engineCode"] = StringParam("The session's confirmed engine code, if a car has been confirmed."),
                ["brand"] = StringParam("The session's confirmed vehicle brand, if known."),
            },
            ["required"] = new JsonArray("systemName"),
        },
    };

    // Declared after the 4 properties above - static field/property
    // initializers run top-to-bottom, so referencing them here before their
    // own initializers had run would silently produce a list of nulls.
    public static List<GeminiFunctionDeclaration> All { get; } =
        [FindCar, SearchByFaultCode, SearchBySymptom, SearchBySystem];

    private static JsonObject StringParam(string description) =>
        new() { ["type"] = "string", ["description"] = description };

    private static JsonObject IntParam(string description) =>
        new() { ["type"] = "integer", ["description"] = description };

    private static JsonObject BoolParam(string description) =>
        new() { ["type"] = "boolean", ["description"] = description };
}
