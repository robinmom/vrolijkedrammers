namespace Drammers.Modules.Content.Shared;

/// <summary>Wie content mag zien (docs/07 §4). Beheren gaat via permissions, niet via zichtbaarheid.</summary>
public enum ContentVisibility
{
    /// <summary>Iedereen, ook gasten zonder account.</summary>
    Public,

    /// <summary>Alleen leden (rol Lid).</summary>
    Members,

    /// <summary>Alleen de doelgroepen in de audience-tabel (fase 5: rollen; groepen en leden volgen in fase 8).</summary>
    Restricted,
}

public enum PublicationStatus
{
    Draft,
    Scheduled,
    Published,
    Archived,
}

public enum AudienceType
{
    Role,
    Group,
    Member,
}

/// <summary>Content met zichtbaarheid en publicatie; basis voor het audience-filter <c>VisibleTo</c>.</summary>
public interface IPublishable
{
    ContentVisibility Visibility { get; }

    PublicationStatus Status { get; }

    DateTime? PublishAt { get; }
}
