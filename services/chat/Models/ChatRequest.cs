namespace ChatService.Models;

// POST /api/chat/stream body. See docs/SemaRepair_Architecture.md section 6.3
// and Rule 5 (confirmed car persists for the whole session).
public class ChatRequest
{
    public string SessionId { get; set; } = "";
    public string Message { get; set; } = "";
    public string? ConfirmedEngineCode { get; set; }
    public string Language { get; set; } = "it";
}
