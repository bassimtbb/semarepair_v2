namespace SearchService.Services;

// Decides whether Rule 8's cross-brand SHARES_ENGINE_WITH fallback is
// allowed for a given System name. See docs/SemaRepair_Architecture.md
// section 5.6 "System Category - Engine Fallback Rules": only Motore and
// Elettrico motore categories allow it - chassis-specific systems (brakes,
// transmission, climate, electrics, steering/suspension) don't transfer
// across brands just because the engine does.
//
// Not exhaustive of every real System name - the doc only lists examples,
// and our actual graph data (services/ingestion-resx) has system names the
// doc never anticipated. Unlisted systems default to NOT allowed: the
// doc's own reasoning argues for caution (wrong-but-confident is worse
// than "ask for a fault code"), so an unrecognized system should never
// silently get the fallback.
//
// The names checked against this set are whatever GetSystemsForFaultAsync
// returned, and that query filters AFFECTS_SYSTEM edges by language - so the
// caller hands us "Injection"/"Inyección"/"Injeção", not "Iniezione",
// whenever the mechanic isn't speaking Italian. With Italian spellings
// alone, default-deny silently made the entire Rule 8 fallback
// Italian-only: P0380 on a shared-engine Citroen returned the document for
// lang=it and not_found for the other four. Each category below therefore
// lists every spelling of itself present in the graph. Adding a language,
// or an ingestion run that introduces a new system name, means adding its
// spellings here too.
public static class SystemCategoryLookup
{
    private static readonly HashSet<string> EngineRelated = new(StringComparer.OrdinalIgnoreCase)
    {
        // Motore (from the doc's section 5.6 example list)
        "Iniezione", "Injection", "Inyección", "Injeção",
        "Alimentazione carburante", "Fuel supply", "Alimentation en carburant",
            "Alimentación de combustible", "Alimentação de combustível",
        // No translations listed: "Candelette" has no AFFECTS_SYSTEM edge in
        // the current graph at all, in any language - it comes from the doc's
        // example list, not from the data.
        "Candelette",
        // Elettrico motore (from the doc's section 5.6 example list) - same
        // situation as "Candelette": doc-only, absent from the graph.
        "Sensori motore",
        "Gestione motore",
        // Not in the doc - added because they're unambiguously engine
        // systems by name, observed in our real graph data. Not an
        // authoritative company answer; revisit if one arrives.
        "Alimentazione motore", "Engine fuel supply", "Alimentation du moteur",
            "Alimentación del motor", "Alimentação do motor",
        "Sistema di accensione", "Ignition system", "Système d'allumage",
            "Sistema de encendido", "Sistema de ignição",
        "Sistema di scarico", "Exhaust system", "Système d'échappement",
            "Sistema de escape",
    };

    public static bool AllowsEngineFallback(string systemName) =>
        EngineRelated.Contains(systemName);
}
