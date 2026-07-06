using SearchService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<GeminiPricing>();
builder.Services.AddSingleton<UsageLogger>();
builder.Services.AddHostedService<UsageLogBackgroundService>();
builder.Services.AddHttpClient<QueryEmbedder>();
builder.Services.AddSingleton<GraphSearchService>();
builder.Services.AddSingleton<VectorSearchService>();
builder.Services.AddSingleton<SymptomSearchService>();
builder.Services.AddSingleton<ValidationService>();
builder.Services.AddSingleton<DocumentContentService>();
builder.Services.AddSingleton<UsageQueryService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

// See docs/SemaRepair_Architecture.md section 9.7 for the health contract.
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "search-service",
    timestamp = DateTimeOffset.UtcNow,
}));

app.Run();
