using Microsoft.AspNetCore.Mvc;
using ChatService.Models;
using ChatService.Services;

namespace ChatService.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly RepairOrchestrator _orchestrator;

    public ChatController(RepairOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    // POST /api/chat/stream - SSE streaming
    [HttpPost("stream")]
    public Task Stream([FromBody] ChatRequest request) =>
        throw new NotImplementedException();

    // POST /api/chat/transcribe - audio transcription via Gemini
    [HttpPost("transcribe")]
    public Task<string> Transcribe() =>
        throw new NotImplementedException();
}
