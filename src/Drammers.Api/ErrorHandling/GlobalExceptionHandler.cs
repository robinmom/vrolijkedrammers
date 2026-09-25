using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.ErrorHandling;

/// <summary>
/// Zet <see cref="DomainException"/> om naar 404/409/422 met code; vangt onverwachte fouten af: logt ze volledig,
/// maar geeft de client alleen een generieke ProblemDetails zonder stacktrace (docs/06 §9, docs/16 §6).
/// </summary>
public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is DomainException domain)
        {
            var status = domain.Kind switch
            {
                DomainErrorKind.NotFound => StatusCodes.Status404NotFound,
                DomainErrorKind.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status422UnprocessableEntity,
            };
            httpContext.Response.StatusCode = status;
            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails { Status = status, Detail = domain.Message, Extensions = { ["code"] = domain.Code } },
            });
        }

        LogUnhandledException(logger, httpContext.Request.Method, httpContext.Request.Path, exception);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Er is een onverwachte fout opgetreden",
                Detail = "Probeer het later opnieuw. Blijft het probleem bestaan, neem dan contact op met de vereniging.",
                Extensions = { ["code"] = ErrorCodes.Unexpected },
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Onverwachte fout bij {Method} {Path}")]
    private static partial void LogUnhandledException(ILogger logger, string method, string path, Exception exception);
}
