namespace ChatService.Models;

// POST /api/chat/stream body. See docs/SemaRepair_Architecture.md section 6.3
// and Rule 5 (confirmed car persists for the whole session).
public class ChatRequest
{
    public string SessionId { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ConfirmedEngineCode { get; set; }

    // Engine codes aren't unique across brands in real data (verified
    // during Search Service work: one engine code spanned 4 brands) - so
    // confirming a car needs both, matching SearchRequest/VehicleQuery's
    // own engineCode+brand scoping.
    public string? ConfirmedBrand { get; set; }

    public string Language { get; set; } = "it";
}
