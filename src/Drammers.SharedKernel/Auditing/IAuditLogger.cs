namespace Drammers.SharedKernel.Auditing;

/// <summary>
/// Schrijft append-only auditregels met hash-keten (docs/04 §11). Neemt deel aan een lopende databasetransactie,
/// zodat de auditregel en de wijziging samen worden vastgelegd; zonder transactie start de logger er zelf een.
/// </summary>
public interface IAuditLogger
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
