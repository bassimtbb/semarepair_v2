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
public static class SystemCategoryLookup
{
    private static readonly HashSet<string> EngineRelated = new(StringComparer.OrdinalIgnoreCase)
    {
        // Motore (from the doc's section 5.6 example list)
        "Iniezione",
        "Alimentazione carburante",
        "Candelette",
        // Elettrico motore (from the doc's section 5.6 example list)
        "Sensori motore",
        "Gestione motore",
        // Not in the doc - added because they're unambiguously engine
        // systems by name, observed in our real graph data. Not an
        // authoritative company answer; revisit if one arrives.
        "Alimentazione motore",
        "Sistema di accensione",
        "Sistema di scarico",
    };

    public static bool AllowsEngineFallback(string systemName) =>
        EngineRelated.Contains(systemName);
}
