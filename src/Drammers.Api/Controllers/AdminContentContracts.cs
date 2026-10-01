using System.ComponentModel.DataAnnotations;
using Drammers.Infrastructure.Content;
using Drammers.Modules.Content.Shared;

namespace Drammers.Api.Controllers;

public sealed record PublicationRequest(
    ContentVisibility Visibility,
    IReadOnlyList<string>? AudienceRoles,
    PublicationStatus Status,
    DateTime? PublishAt,
    IReadOnlyList<Guid>? AudienceGroups = null,
    IReadOnlyList<Guid>? AudienceMembers = null)
{
    public PublicationInput ToInput() =>
        new(Visibility, AudienceRoles ?? [], Status, PublishAt?.ToUniversalTime(), AudienceGroups ?? [], AudienceMembers ?? []);
}

public sealed record EventRequest(
    int CategoryId,
    [Required, StringLength(200, MinimumLength = 2)] string Title,
    [StringLength(500)] string? Summary,
    [StringLength(20000)] string? Description,
    DateTime StartAt,
    DateTime? EndAt,
    bool AllDay,
    [StringLength(200)] string? LocationName,
    [StringLength(300)] string? LocationAddress,
    [Range(-90, 90)] decimal? Latitude,
    [Range(-180, 180)] decimal? Longitude,
    bool IsHighlight,
    [StringLength(40)] string? BadgeText,
    [Required] PublicationRequest Publication,
    bool AccessControl = false)
{
    public EventInput ToInput() => new(CategoryId, Title, Summary, Description, StartAt.ToUniversalTime(), EndAt?.ToUniversalTime(), AllDay,
        LocationName, LocationAddress, Latitude, Longitude, IsHighlight, BadgeText, Publication.ToInput(), AccessControl);
}

public sealed record NewsRequest(
    [Required, StringLength(200, MinimumLength = 2)] string Title,
    [StringLength(500)] string? Summary,
    [Required, StringLength(50000)] string Body,
    [StringLength(50)] string? Category,
    DateTime? ExpireAt,
    [Required] PublicationRequest Publication,
    bool PushOnPublish = false,
    bool ShowOnWebsite = false,
    [StringLength(100000)] string? WebsiteBody = null,
    [StringLength(100)] string? Slug = null,
    [StringLength(300)] string? Image = null)
{
    public NewsInput ToInput() => new(Title, Summary, Body, Category, ExpireAt?.ToUniversalTime(), Publication.ToInput(), PushOnPublish,
        ShowOnWebsite, WebsiteBody, Slug, Image);
}

/// <summary>Een vooraf geüploade afbeelding: stuur <see cref="Path"/> mee bij het opslaan; <see cref="Url"/> is voor het voorbeeld.</summary>
public sealed record UploadedImageResponse(string Path, string Url);

public sealed record AlbumRequest(
    [Required, StringLength(200, MinimumLength = 2)] string Title,
    DateOnly? AlbumDate,
    [StringLength(2000)] string? Description,
    Guid? EventId,
    [Required] PublicationRequest Publication,
    Modules.Content.Photos.PhotoCategory Category = Modules.Content.Photos.PhotoCategory.Other)
{
    public AlbumInput ToInput() => new(Title, AlbumDate, Description, EventId, Publication.ToInput(), Category);
}

public sealed record PublicationResponse(
    ContentVisibility Visibility, IReadOnlyList<string> AudienceRoles, PublicationStatus Status, DateTime? PublishAt,
    IReadOnlyList<Guid> AudienceGroups, IReadOnlyList<Guid> AudienceMembers)
{
    /// <summary>Bouwt het antwoord uit de audience-rijen van een event, nieuwsbericht of album.</summary>
    public static PublicationResponse From(
        ContentVisibility visibility, IEnumerable<(AudienceType Type, string Ref)> audiences, PublicationStatus status, DateTime? publishAt)
    {
        var list = audiences.ToList();
        return new PublicationResponse(visibility,
            [.. list.Where(a => a.Type == AudienceType.Role).Select(a => a.Ref)], status, publishAt,
            [.. list.Where(a => a.Type == AudienceType.Group).Select(a => Guid.Parse(a.Ref))],
            [.. list.Where(a => a.Type == AudienceType.Member).Select(a => Guid.Parse(a.Ref))]);
    }
}

public sealed record CreatedResponse(Guid Id);
