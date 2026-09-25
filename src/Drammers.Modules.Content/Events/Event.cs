using Drammers.Modules.Content.Shared;
using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Content.Events;

public sealed class EventCategory
{
    public int Id { get; set; }

    public required string Code { get; set; }

    public required string Name { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>Activiteit in de agenda/het programma (docs/04 §5).</summary>
public sealed class Event : IAuditable, IPublishable
{
    public Guid Id { get; set; }

    public int CarnivalYearId { get; set; }

    public int CategoryId { get; set; }

    public required string Title { get; set; }

    public string? Summary { get; set; }

    /// <summary>Markdown; de API levert daarnaast gesanitizede HTML.</summary>
    public string? Description { get; set; }

    public DateTime StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    public bool AllDay { get; set; }

    public string? LocationName { get; set; }

    public string? LocationAddress { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    /// <summary>Pad in de container <c>content</c>; door de server gegenereerd.</summary>
    public string? ImageBlobPath { get; set; }

    public ContentVisibility Visibility { get; set; }

    public PublicationStatus Status { get; set; }

    public DateTime? PublishAt { get; set; }

    public bool IsHighlight { get; set; }

    public string? BadgeText { get; set; }

    public List<EventAudience> Audiences { get; set; } = [];

    public List<EventAttachment> Attachments { get; set; } = [];

    public byte[] RowVersion { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

public sealed class EventAudience
{
    public Guid EventId { get; set; }

    public AudienceType AudienceType { get; set; }

    /// <summary>Bij <see cref="AudienceType.Role"/>: de rolcode.</summary>
    public required string AudienceRef { get; set; }
}

public sealed class EventAttachment
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public required string FileName { get; set; }

    public required string BlobPath { get; set; }

    public required string ContentType { get; set; }

    public long SizeBytes { get; set; }
}
