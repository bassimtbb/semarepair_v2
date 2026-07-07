using ChatService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<GeminiPricing>();
builder.Services.AddSingleton<UsageLogger>();
builder.Services.AddHostedService<UsageLogBackgroundService>();
builder.Services.AddHttpClient<RepairOrchestrator>();
builder.Services.AddHttpClient<GeminiChatClient>();
// 10s hard timeout on the Google TTS call per §6.4.
builder.Services.AddHttpClient<GoogleCloudTtsService>(client =>
    client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

// See docs/SemaRepair_Architecture.md section 9.7 for the health contract.
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "chat-service",
    timestamp = DateTimeOffset.UtcNow,
}));

app.Run();
