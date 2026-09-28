namespace Drammers.SharedKernel.Errors;

/// <summary>
/// Verwachte, voor de gebruiker begrijpelijke fout (validatie, conflict, niet gevonden). De API zet hem om naar
/// ProblemDetails met <see cref="Code"/> en de Nederlandse <c>detail</c> (docs/05 §1).
/// </summary>
public sealed class DomainException(string code, string message, DomainErrorKind kind = DomainErrorKind.Validation) : Exception(message)
{
    public string Code { get; } = code;

    public DomainErrorKind Kind { get; } = kind;

    /// <summary>Meldingen per veld (bijv. een optochtinschrijving); in ProblemDetails als <c>issues</c>.</summary>
    public IReadOnlyList<FieldIssue>? Issues { get; init; }
}

/// <summary>Een melding bij een veld, met ernst <c>Block</c> of <c>Warn</c>.</summary>
public sealed record FieldIssue(string Field, string Message, string Severity);

public enum DomainErrorKind
{
    Validation,
    NotFound,
    Conflict,

    /// <summary>Wel het recht op de actie, maar niet voor deze inhoud (bijv. een doelgroep buiten de eigen groepen).</summary>
    Forbidden,

    /// <summary>De gegevens zijn intussen door iemand anders gewijzigd (optimistic concurrency, 412).</summary>
    PreconditionFailed,
}
