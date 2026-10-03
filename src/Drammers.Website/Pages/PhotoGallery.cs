using Drammers.Website.Content;

namespace Drammers.Website.Pages;

/// <summary>Model voor de fotogrid met lightbox (<c>_PhotoGallery</c>).</summary>
public sealed record PhotoGallery(string Title, IReadOnlyList<PhotoView> Photos);
