using ChinookCheckers.Application.DTOs;
using ChinookCheckers.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ChinookCheckers.API.Controllers;

[ApiController]
[Route("healthz")]
[Produces("application/json")]
public sealed class HealthController(ICheckersEnginePool enginePool) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<HealthResponse> Get()
    {
        var workers = enginePool.WorkerCount;
        var response = new HealthResponse(enginePool.IsReady && workers > 0, workers);
        return response.Ok ? Ok(response) : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }
}
