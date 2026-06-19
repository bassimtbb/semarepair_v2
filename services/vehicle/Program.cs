using VehicleService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<VehicleSearchService>();

var app = builder.Build();

app.MapControllers();

// See docs/SemaRepair_Architecture.md section 9.7 for the health contract.
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "vehicle-service",
    timestamp = DateTimeOffset.UtcNow,
}));

app.Run();
