using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Drammers.Modules.Audit.AuditLog;

namespace Drammers.Infrastructure.Auditing;

/// <summary>
/// Hash-keten over de auditlog (tamper-evidence, docs/04 §11): elke regel bevat de SHA-256 van zijn inhoud plus de hash
/// van de vorige regel. Een gewijzigde of verwijderde regel breekt de keten vanaf dat punt.
/// </summary>
public static class AuditChain
{
    private const char Separator = '\u001f';

    public static string ComputeHash(AuditLogEntry entry)
    {
        var canonical = string.Join(
            Separator,
            entry.HashPrev ?? string.Empty,
            entry.OccurredAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            entry.ActorUserId?.ToString("D") ?? string.Empty,
            entry.ActorType.ToString(),
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.OldValues ?? string.Empty,
            entry.NewValues ?? string.Empty,
            entry.IpHash ?? string.Empty,
            entry.DeviceId?.ToString("D") ?? string.Empty,
            entry.CorrelationId ?? string.Empty);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>Controleert de keten in volgorde van <c>Id</c>; geeft het eerste afwijkende Id terug, of <c>null</c>.</summary>
    public static long? FindFirstBrokenEntry(IEnumerable<AuditLogEntry> entriesInOrder)
    {
        string? previousHash = null;
        foreach (var entry in entriesInOrder)
        {
            if (entry.HashPrev != previousHash || entry.Hash != ComputeHash(entry))
            {
                return entry.Id;
            }

            previousHash = entry.Hash;
        }

        return null;
    }
}
