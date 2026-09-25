namespace Drammers.SharedKernel.Auditing;

/// <summary>
/// Eén auditregel. <paramref name="OldValues"/> en <paramref name="NewValues"/> bevatten alleen gewijzigde velden
/// (JSON), met gevoelige velden gemaskeerd door de aanroeper (docs/04 §11).
/// </summary>
public sealed record AuditEntry(
    string Action,
    string EntityType,
    string EntityId,
    string? OldValues = null,
    string? NewValues = null);
