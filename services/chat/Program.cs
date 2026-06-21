using ChatService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient<RepairOrchestrator>();
builder.Services.AddHttpClient<GeminiChatClient>();
builder.Services.AddSingleton<SessionStore>();

var app = builder.Build();

app.MapControllers();

// See docs/SemaRepair_Architecture.md section 9.7 for the health contract.
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "chat-service",
    timestamp = DateTimeOffset.UtcNow,
}));

app.Run();
