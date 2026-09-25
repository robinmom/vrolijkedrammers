using System.Diagnostics;
using Drammers.SharedKernel.Errors;

namespace Drammers.Api.ErrorHandling;

/// <summary>
/// Vult elke ProblemDetails-respons (RFC 9457) aan met <c>traceId</c> en een machineleesbare <c>code</c> (docs/05 §1).
/// </summary>
public static class ProblemDetailsDefaults
{
    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;

        if (!problem.Extensions.ContainsKey("code"))
        {
            problem.Extensions["code"] = problem.Status switch
            {
                StatusCodes.Status401Unauthorized => ErrorCodes.Unauthorized,
                StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
                StatusCodes.Status404NotFound => ErrorCodes.NotFound,
                StatusCodes.Status400BadRequest or StatusCodes.Status422UnprocessableEntity => ErrorCodes.Validation,
                _ => ErrorCodes.Unexpected,
            };
        }
    }
}
