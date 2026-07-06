using System.Collections.Concurrent;
using ChatService.Models;

namespace ChatService.Services;

// In-memory session state - see the explicit decision on Session.cs not to
// add a persistence layer yet. Registered as a singleton so all requests
// share the same dictionary; a session is expected to be driven by one
// mechanic's single in-flight conversation at a time, so concurrent
// mutation of the same session's History is an accepted, unhandled edge
// case at this scale, not a gap to engineer around now.
public class SessionStore
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public Session GetOrCreate(string sessionId)
    {
        var session = _sessions.GetOrAdd(sessionId, id => new Session { SessionId = id });
        session.LastAccessedAt = DateTimeOffset.UtcNow;
        return session;
    }

    // Rule 6: reset clears confirmed car AND session History in-place so
    // any in-flight orchestrator turn holding the session reference also
    // sees the cleared state. TryRemove was the prior approach but left
    // a race window: the next POST could arrive before the DELETE, get the
    // old session from GetOrCreate, and pass ghost History to Gemini.
    public void Reset(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return;
        session.ConfirmedCodiceMotore = null;
        session.ConfirmedMarca = null;
        session.ConfirmedCarId = null;
        session.ConfirmedCarLabel = null;
        session.History.Clear();
    }

    // Dead code: RepairOrchestrator manages session fields directly
    // (ConfirmCarAsync/StoreConfirmedCar) and never calls this method -
    // confirmed live by grepping for callers. Kept in sync with the
    // Italian field rename anyway since removing unused-but-harmless code
    // wasn't asked for here.
    public void ConfirmCar(string sessionId, string codiceMotore, string? marca, string? carLabel)
    {
        var session = GetOrCreate(sessionId);
        session.ConfirmedCodiceMotore = codiceMotore;
        session.ConfirmedMarca = marca;
        session.ConfirmedCarLabel = carLabel;
    }
}
