namespace ChatService.Models;

// Per-session state, kept in-memory (SessionStore) for the lifetime of the
// Chat Service process - see the explicit decision to skip a persistence
// layer for now (docs/SemaRepair_Architecture.md section 13's own
// unresolved Redis question). Lost on restart; doesn't work if Chat
// Service ever scales beyond one instance.
//
// Note what this does NOT need: a separate "pending symptom/fault code"
// field for Rule 7's re-search-after-car-confirmation flow. Gemini already
// sees the mechanic's original message in History on every subsequent
// call, so once it's told a car is now confirmed, it naturally re-issues
// the same search tool call with the engine code filled in - the
// conversation history itself is the replay mechanism, not extra state.
public class Session
{
    public string SessionId { get; set; } = "";

    // Rule 5: persists for the entire session. Rule 8 needs Marca too,
    // since engine codes aren't unique across brands.
    public string? ConfirmedCodiceMotore { get; set; }
    public string? ConfirmedMarca { get; set; }

    // idMacchina of the specific confirmed car, when known - the only
    // unambiguous identity, since codiceMotore+marca alone can still match
    // several trims (e.g. IVECO Daily III 35C-13/40C-13/45C-13 all share
    // engine 8140.43S). Used to detect a *different car* confirmation even
    // when the engine code happens to be the same - see RepairOrchestrator.
    public string? ConfirmedCarId { get; set; }

    // Human-readable label for Rule 4's confirmation message, e.g.
    // "FIAT Ducato 2.3 JTD 16v (F1AE0481C)" - assembled once from the
    // FindCar/Vehicle Service result that confirmed this car.
    public string? ConfirmedCarLabel { get; set; }

    // Full Gemini conversation history (both routing-call and
    // formatting-call turns are kept separate - see RepairOrchestrator),
    // needed verbatim on every subsequent call for multi-turn function
    // calling and for Gemini to recall earlier messages (Rule 5/7).
    public List<GeminiContent> History { get; set; } = [];

    public DateTimeOffset LastAccessedAt { get; set; } = DateTimeOffset.UtcNow;
}
