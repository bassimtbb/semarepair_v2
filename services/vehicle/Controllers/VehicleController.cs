using Microsoft.AspNetCore.Mvc;
using VehicleService.Models;
using VehicleService.Services;

namespace VehicleService.Controllers;

[ApiController]
[Route("api/vehicles")]
public class VehicleController : ControllerBase
{
    private readonly VehicleSearchService _vehicleSearch;

    public VehicleController(VehicleSearchService vehicleSearch)
    {
        _vehicleSearch = vehicleSearch;
    }

    // GET /api/vehicles?brand=FIAT&model=Ducato&yearFrom=2000&yearTo=2002&fuel=Diesel
    // GET /api/vehicles?engineCode=F1AE0481C
    [HttpGet]
    public Task<VehicleResponse> Search([FromQuery] VehicleQuery query) =>
        _vehicleSearch.SearchAsync(query);

    // GET /api/vehicles/{idMacchina}
    [HttpGet("{idMacchina}")]
    public Task<VehicleResult?> GetById(string idMacchina) =>
        _vehicleSearch.GetByIdAsync(idMacchina);
}
