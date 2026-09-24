namespace Drammers.SharedKernel.Identifiers;

/// <summary>
/// Genereert primaire sleutels als UUIDv7: tijd-geordend, dus index-vriendelijk in SQL Server (docs/04 §1).
/// </summary>
public static class IdGenerator
{
    public static Guid NewId() => Guid.CreateVersion7();
}
