using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Content.Tasks;

/// <summary>Categorie van de takenlijst (2026-10-15), zelf beheerd door het bestuur (bijv. Pronkzitting, Optocht).</summary>
public sealed class TaskCategory
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public int SortOrder { get; set; }

    public bool Active { get; set; } = true;
}

public enum CarnivalTaskStatus
{
    NotStarted,
    InProgress,
    Done,
    Cancelled,
}

/// <summary>
/// Een taak voor de voorbereiding van carnaval in een carnavalsjaar (2026-10-15). Voortgang: nog niet gestart 0%, bezig het
/// ingevulde percentage, afgerond 100%; geannuleerd telt niet mee. Elk jaar kopieert het bestuur de taken die terugkomen.
/// </summary>
public sealed class CarnivalTask : IAuditable
{
    public Guid Id { get; set; }

    public int CarnivalYearId { get; set; }

    public Guid CategoryId { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public CarnivalTaskStatus Status { get; set; }

    /// <summary>Voortgang 0–100 (alleen betekenisvol bij <see cref="CarnivalTaskStatus.InProgress"/>).</summary>
    public int Progress { get; set; }

    /// <summary>Automatisch gezet zodra de taak op "bezig" (of meteen "afgerond") gaat.</summary>
    public DateOnly? StartedOn { get; set; }

    public DateOnly? DueOn { get; set; }

    public DateOnly? CompletedOn { get; set; }

    /// <summary>De taak van vorig jaar waar deze een kopie van is.</summary>
    public Guid? CopiedFromId { get; set; }

    public List<CarnivalTaskAssignee> Assignees { get; set; } = [];

    public byte[] RowVersion { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>Bijdrage aan het totaal; <c>null</c> voor geannuleerd.</summary>
    public int? EffectiveProgress => Status switch
    {
        CarnivalTaskStatus.Done => 100,
        CarnivalTaskStatus.InProgress => Progress,
        CarnivalTaskStatus.NotStarted => 0,
        _ => null,
    };
}

/// <summary>Een lid dat aan een taak werkt.</summary>
public sealed class CarnivalTaskAssignee
{
    public Guid TaskId { get; set; }

    public Guid MemberId { get; set; }
}
