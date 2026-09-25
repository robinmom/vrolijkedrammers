namespace Drammers.SharedKernel.Errors;

/// <summary>
/// Verwachte, voor de gebruiker begrijpelijke fout (validatie, conflict, niet gevonden). De API zet hem om naar
/// ProblemDetails met <see cref="Code"/> en de Nederlandse <c>detail</c> (docs/05 §1).
/// </summary>
public sealed class DomainException(string code, string message, DomainErrorKind kind = DomainErrorKind.Validation) : Exception(message)
{
    public string Code { get; } = code;

    public DomainErrorKind Kind { get; } = kind;
}

public enum DomainErrorKind
{
    Validation,
    NotFound,
    Conflict,
}
