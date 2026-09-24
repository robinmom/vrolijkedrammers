namespace Drammers.SharedKernel.Errors;

/// <summary>
/// Machineleesbare foutcodes voor de <c>code</c>-extensie van ProblemDetails (docs/05 §1 en §10).
/// </summary>
public static class ErrorCodes
{
    public const string Unexpected = "UNEXPECTED_ERROR";
    public const string NotFound = "NOT_FOUND";
    public const string Validation = "VALIDATION_FAILED";
}
