using Drammers.Modules.Content.Shared;
using Drammers.SharedKernel.Persistence;

namespace Drammers.Modules.Content.Photos;

/// <summary>Fotoalbum (docs/04 §5). Een album is zichtbaar zodra het gepubliceerd is.</summary>
public sealed class PhotoAlbum : IAuditable, IPublishable
{
    public Guid Id { get; set; }

    public int? CarnivalYearId { get; set; }

    public Guid? EventId { get; set; }

    public required string Title { get; set; }

    public DateOnly? AlbumDate { get; set; }

    public string? Description { get; set; }

    public ContentVisibility Visibility { get; set; }

    public PublicationStatus Status { get; set; }

    public DateTime? PublishAt { get; set; }

    public Guid? CoverPhotoId { get; set; }

    public List<PhotoAlbumAudience> Audiences { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}

public sealed class PhotoAlbumAudience
{
    public Guid AlbumId { get; set; }

    public AudienceType AudienceType { get; set; }

    public required string AudienceRef { get; set; }
}

public enum PhotoProcessingStatus
{
    /// <summary>Geüpload naar quarantaine; wacht op scan en verwerking.</summary>
    Pending,

    /// <summary>Derivaten (1600 px en thumbnail, zonder EXIF/GPS) staan klaar.</summary>
    Ready,

    /// <summary>Geweigerd door de malwarescan of niet te verwerken; bestanden verwijderd.</summary>
    Rejected,
}

public sealed class Photo
{
    public Guid Id { get; set; }

    public Guid AlbumId { get; set; }

    public required string OriginalBlobPath { get; set; }

    public string? DisplayBlobPath { get; set; }

    public string? ThumbnailBlobPath { get; set; }

    public PhotoProcessingStatus ProcessingStatus { get; set; }

    public int SortOrder { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public DateTime? TakenAt { get; set; }

    public string? Caption { get; set; }

    public string? Photographer { get; set; }

    /// <summary>Verborgen (bijv. portretrecht-verzoek): direct onzichtbaar.</summary>
    public bool Hidden { get; set; }

    public Guid? UploadedBy { get; set; }

    public DateTime UploadedAt { get; set; }
}
