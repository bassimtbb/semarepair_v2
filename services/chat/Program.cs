using ChatService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpClient<RepairOrchestrator>();

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
