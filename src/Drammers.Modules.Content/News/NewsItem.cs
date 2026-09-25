using Drammers.Modules.Content.Shared;
using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Content.News;

/// <summary>Nieuwsbericht (docs/04 §5). Push bij publicatie volgt in fase 10.</summary>
public sealed class NewsItem : IAuditable, IPublishable
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public string? Summary { get; set; }

    /// <summary>Markdown; de API levert daarnaast gesanitizede HTML.</summary>
    public required string Body { get; set; }

    public string? ImageBlobPath { get; set; }

    public Guid? AuthorUserId { get; set; }

    public string? Category { get; set; }

    public DateTime? PublishAt { get; set; }

    /// <summary>Na dit moment niet meer zichtbaar in de app.</summary>
    public DateTime? ExpireAt { get; set; }

    public ContentVisibility Visibility { get; set; }

    public PublicationStatus Status { get; set; }

    public List<NewsAudience> Audiences { get; set; } = [];

    public byte[] RowVersion { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

public sealed class NewsAudience
{
    public Guid NewsId { get; set; }

    public AudienceType AudienceType { get; set; }

    public required string AudienceRef { get; set; }
}
