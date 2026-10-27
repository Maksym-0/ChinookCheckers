using ChinookCheckers.Application.Exceptions;
using ChinookCheckers.Core.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ChinookCheckers.API.Middleware;

/// <summary>Maps domain and engine exceptions to RFC 7807 problem responses.</summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    private const int ClientClosedRequest = 499;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        var (status, title) = exception switch
        {
            InvalidPositionException => (StatusCodes.Status422UnprocessableEntity, "Invalid position."),
            EngineUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Engine is unavailable."),
            EngineException => (StatusCodes.Status500InternalServerError, "Engine failure."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected server error.")
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Request failed with {StatusCode}", status);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status == StatusCodes.Status422UnprocessableEntity ? exception.Message : null
            }
        });
    }
}
