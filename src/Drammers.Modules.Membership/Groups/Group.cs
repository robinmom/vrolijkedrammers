using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Membership.Groups;

public enum GroupType
{
    DanceGuard,
    Committee,
    ParadeGroup,
    Other,
}

public enum GroupFunction
{
    Member,
    Lead,
}

/// <summary>
/// Doelgroep die geen rol is, bijv. "Jeugdcommissie" of "Dansgarde meisjes" (docs/04 §4). Een rol geeft rechten; een
/// groep bepaalt alleen wie content (en later meldingen) ontvangt.
/// </summary>
public sealed class Group : IAuditable
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    public GroupType Type { get; set; }

    /// <summary>Optioneel: alleen voor dit carnavalsjaar (bijv. een optochtgroep).</summary>
    public int? CarnivalYearId { get; set; }

    public bool Active { get; set; } = true;

    public List<GroupMembership> Memberships { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

/// <summary>Lid in een groep, optioneel tijdelijk (bijv. alleen dit seizoen).</summary>
public sealed class GroupMembership
{
    public Guid GroupId { get; set; }

    public Guid MemberId { get; set; }

    public GroupFunction Function { get; set; }

    public DateOnly? ValidFrom { get; set; }

    public DateOnly? ValidTo { get; set; }

    public bool IsValidOn(DateOnly date) => (ValidFrom is null || ValidFrom <= date) && (ValidTo is null || ValidTo >= date);
}
