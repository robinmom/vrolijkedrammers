namespace Drammers.SharedKernel.Persistence;

/// <summary>
/// Muteerbare entiteit met auditkolommen (docs/04 §1). De waarden worden bij het opslaan automatisch gezet;
/// code hoeft ze niet zelf te vullen.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }

    Guid? CreatedBy { get; set; }

    DateTime? UpdatedAt { get; set; }

    Guid? UpdatedBy { get; set; }
}
