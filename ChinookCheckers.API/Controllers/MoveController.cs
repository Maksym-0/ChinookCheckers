using ChinookCheckers.Application.DTOs;
using ChinookCheckers.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ChinookCheckers.API.Controllers;

[ApiController]
[Route("v1/move")]
[Produces("application/json")]
public sealed class MoveController(
    IMoveService moveService,
    ISearchLimitsResolver limitsResolver,
    ILogger<MoveController> logger) : ControllerBase
{
    [HttpPost("suggest")]
    [ProducesResponseType<SuggestMoveResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<SuggestMoveResponse>> Suggest(
        [FromBody] SuggestMoveRequest request,
        CancellationToken cancellationToken)
    {
        var hardTimeout = limitsResolver.ResolveHardTimeout(request.Limits);
        using var timeoutCts = new CancellationTokenSource(hardTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return Ok(await moveService.SuggestMoveAsync(request, linkedCts.Token));
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Move search for {Position} exceeded hardTimeMs={HardTimeMs}",
                request.State.Position,
                (long)hardTimeout.TotalMilliseconds);

            return Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Engine search timed out.",
                detail: $"No move was produced within hardTimeMs={(long)hardTimeout.TotalMilliseconds}.");
        }
    }

    [HttpPost("validate")]
    [ProducesResponseType<ValidateMoveResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public ActionResult<ValidateMoveResponse> Validate([FromBody] ValidateMoveRequest request) =>
        Ok(moveService.ValidateMove(request));
}
