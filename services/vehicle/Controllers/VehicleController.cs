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

    // GET /api/vehicles?marca=FIAT&modello=Ducato&annoInizio=2000&annoFine=2002&alimentazione=Diesel
    // GET /api/vehicles?marca=FORD&modello=Ecosport&motorizzazione=1.5 TDCi
    // GET /api/vehicles?codiceMotore=F1AE0481C
    // Italian query params - a deliberate deviation from
    // docs/SemaRepair_Architecture.md section 6.5 (English params), see
    // progress.md's decisions log.
    [HttpGet]
    public Task<VehicleResponse> Search([FromQuery] VehicleQuery query) =>
        _vehicleSearch.SearchAsync(query);

    // GET /api/vehicles/{idMacchina}
    [HttpGet("{idMacchina}")]
    public async Task<IActionResult> GetById(string idMacchina)
    {
        var result = await _vehicleSearch.GetByIdAsync(idMacchina);
        return result is null ? NotFound() : Ok(result);
    }
}
