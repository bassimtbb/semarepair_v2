using SearchService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<GraphSearchService>();
builder.Services.AddSingleton<VectorSearchService>();
builder.Services.AddSingleton<SymptomSearchService>();
builder.Services.AddSingleton<ValidationService>();

var app = builder.Build();

app.MapControllers();

// See docs/SemaRepair_Architecture.md section 9.7 for the health contract.
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "search-service",
    timestamp = DateTimeOffset.UtcNow,
}));

app.Run();
