using ChatService.Models;

namespace ChatService.Services;

// Gemini function calling orchestration: FindCar, SearchByFaultCode,
// SearchBySymptom. See docs/SemaRepair_Architecture.md section 2.4 and
// the full decision tree in section 5.10.
public class RepairOrchestrator
{
    private readonly HttpClient _httpClient;

    public RepairOrchestrator(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public IAsyncEnumerable<ChatResponse> HandleMessageAsync(ChatRequest request) =>
        throw new NotImplementedException();
}
