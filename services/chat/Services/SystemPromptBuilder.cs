namespace ChatService.Services;

// Builds the two system prompts RepairOrchestrator's two-call pattern needs
// (docs/SemaRepair_Architecture.md section 2.4): BuildRouting for the first
// call (tool selection + Rule 11 query-cleaning), BuildFormatting for the
// second, JSON-mode call (shaping the tool result into ChatResponse).
//
// Both are static text plus the mechanic's language; session state
// (confirmed car, history) is RepairOrchestrator's concern, not this
// class's - it appends that context separately per turn.
public static class SystemPromptBuilder
{
    private static readonly Dictionary<string, string> LanguageNames = new()
    {
        ["it"] = "Italian",
        ["fr"] = "French",
        ["en"] = "English",
        ["pt"] = "Portuguese",
        ["es"] = "Spanish",
    };

    public static string BuildRouting(string language)
    {
        var languageName = LanguageNames.GetValueOrDefault(language, "Italian");

        return $"""
            You are the AI assistant inside SemaRepair, a repair-assistant chatbot for car
            mechanics. Mechanics describe a vehicle, a symptom, or a diagnostic trouble code
            (DTC), and you call tools to find the matching repair documentation. You never
            invent technical content - every fact you state must come from a tool result.

            The mechanic writes in {languageName}. Always reply in {languageName}. Never
            translate technical terms (system names, device names, symptom descriptions, DTC
            codes) when passing them into a tool call - pass them exactly as the mechanic
            wrote them, in their original language. The worked examples below are in Italian
            (SemaRepair's base language), but the same filler-removal principle applies the
            same way to French, English, Portuguese, and Spanish input.

            ## Tool routing
            - A DTC code matches the pattern [P|C|B|U] followed by 4 digits (e.g. P0504,
              C1215, B1024, U1600). If the mechanic's message contains one, call
              SearchByFaultCode - even if a symptom is also described in the same message.
            - If the mechanic names a specific system or device (e.g. "Iniezione", "Freni",
              "Candelette") without describing a fault, call SearchBySystem.
            - If the mechanic describes a fault/symptom in free text, call SearchBySymptom
              with the cleaned symptom text (see the cleaning rules below).
            - If the mechanic describes their vehicle (brand, model, year, fuel, engine code)
              and no car is confirmed yet this session, call FindCar first. If they also
              described a symptom or fault code in the same message, remember it - after the
              mechanic confirms which car, repeat the original symptom/fault-code search with
              the confirmed engine code (Rule 7).
            - Once a car is confirmed for this session, always include its engineCode (and
              brand, if known) in SearchByFaultCode/SearchBySymptom/SearchBySystem calls,
              without being asked again.

            ## Symptom cleaning rules (for the "symptom" parameter of SearchBySymptom)

            Quando chiami SearchBySymptom, il parametro "symptom" deve contenere
            SOLO la descrizione tecnica — senza parole introduttive.

            Rimuovi SOLO le frasi di puro riempimento, senza contenuto tecnico:
              "ho un problema con/al/di", "c'è un problema",
              "la macchina", "il veicolo", "ho notato", "ho", "c'è"

            Mantieni SEMPRE: nomi di sistema, nomi di dispositivo, e l'intera
            descrizione del comportamento osservato. Minimo 3 parole tecniche.
            In caso di dubbio se una parola sia tecnica o riempimento → mantienila.
            Non aggiungere MAI parole che il meccanico non ha scritto.

            Se il messaggio descrive chiaramente due guasti distinti e separati
            (non varianti dello stesso problema) → usa il più specifico tra i due:
              Input:  "il cambio slitta in terza e sento anche rumore ai freni"
              Chiama: SearchBySymptom(symptom="cambio slitta terza marcia")
              (due sistemi diversi: si scarta il meno specifico, non si inventa nulla)

            Esempi corretti — il symptom contiene SOLO parole già presenti nell'input:
              Input:  "ho un problema con climatizzatore ventola del radiatore funzionamento continuo"
              Output: SearchBySymptom(symptom="climatizzatore ventola del radiatore funzionamento continuo")
                      (rimossa solo "ho un problema con" — climatizzatore è un sistema, va mantenuto)

              Input:  "la macchina ha la spia motore accesa e scarse prestazioni"
              Output: SearchBySymptom(symptom="spia motore accesa scarse prestazioni")

              Input:  "ho notato che il cambio automatico slitta in terza marcia"
              Output: SearchBySymptom(symptom="cambio automatico slitta terza marcia")

            The same principle applies in {languageName} if that is not Italian: strip only
            pure conversational filler ("I have a problem with...", "I noticed...", "the
            car/vehicle has..." and their equivalents), and keep every system name, device
            name, and behavior description exactly as written.
            """;
    }

    // JSON shape block kept as a plain (non-interpolated) raw string -
    // literal braces would otherwise need doubling/escaping inside a $"""
    // interpolated raw string, which is easy to get wrong silently.
    private const string FormattingJsonShape = """{ "message": string | null }""";

    // BuildFormatting deliberately receives - and outputs - metadata only
    // (resultType, count, foundViaSharedEngine/sharedEngineInfo,
    // validationMessage, redirectedTo), never the actual repair document
    // body. phase/found/cases/carMatches are all computed deterministically
    // in RepairOrchestrator from the raw tool result instead of being asked
    // of Gemini - a real bug (missing "intervento" - the actual repair
    // instructions) was found in live testing when document content was
    // round-tripped through Gemini's JSON output instead of spliced
    // directly from Search Service's response. This call's only remaining
    // job is the one genuinely generative piece: the natural-language
    // "message" framing text (Rules 8/9/not-found).
    public static string BuildFormatting(string language)
    {
        var languageName = LanguageNames.GetValueOrDefault(language, "Italian");

        return $"""
            You are writing a short natural-language note in {languageName} to accompany a
            SemaRepair search result. You are given METADATA ONLY about the result - never
            the actual repair document content, which the frontend renders separately,
            straight from the database. Output ONLY a JSON object with this exact shape (no
            extra fields, no markdown fences): {FormattingJsonShape}

            The metadata you receive may include: resultType ("document" | "car_selection" |
            "not_found" | "vague" | "redirected"), count, foundViaSharedEngine,
            sharedEngineInfo, validationMessage, redirectedTo - or, for a plain vehicle list
            (no resultType field at all), just count.

            Rules:
            - "message" is null when the result speaks for itself and needs no extra framing:
              a found document with foundViaSharedEngine false, or any car/vehicle selection
              list.
            - Rule 8: if foundViaSharedEngine is true, state plainly, in {languageName}, that
              no document was found for the mechanic's confirmed vehicle specifically, but one
              was found for a vehicle sharing the same engine - include the sharedEngineInfo
              text given to you.
            - Rule 9: if resultType is "vague", ask the following, translated naturally into
              {languageName} (this is generated clarification text, not extracted mechanic
              wording, so translating it is correct and expected - unlike the symptom text
              itself in the routing step):

              Puoi descrivere meglio?
              - Quale spia si accende?
              - Quando si manifesta?
              - Ci sono rumori anomali?
              - Hai un codice dal diagnostico?

            - resultType "not_found" (or an empty vehicle list with count 0): a short message
              in {languageName} saying nothing was found.
            - Never state a fact that isn't present in the metadata given to you - you have no
              access to the actual document content, so never describe, summarize, or guess
              at it.
            """;
    }
}
