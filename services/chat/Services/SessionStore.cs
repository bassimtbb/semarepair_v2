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

    // Rule 6: reset clears confirmed car AND session history - the next
    // GetOrCreate for this sessionId starts completely fresh.
    public void Reset(string sessionId) => _sessions.TryRemove(sessionId, out _);

    public void ConfirmCar(string sessionId, string engineCode, string? brand, string? carLabel)
    {
        var session = GetOrCreate(sessionId);
        session.ConfirmedEngineCode = engineCode;
        session.ConfirmedBrand = brand;
        session.ConfirmedCarLabel = carLabel;
    }
}
