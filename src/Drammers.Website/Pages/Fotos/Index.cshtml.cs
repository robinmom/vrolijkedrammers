using Drammers.Modules.Content.Photos;
using Drammers.Website.Content;

namespace Drammers.Website.Pages.Fotos;

public sealed class IndexModel(WebsiteReader reader) : SitePage
{
    public static readonly (PhotoCategory Category, string Slug, string Name)[] Categories =
    [
        (PhotoCategory.Pronkzitting, "pronkzitting", "Pronkzitting"),
        (PhotoCategory.Carnival, "carnaval", "Carnaval"),
        (PhotoCategory.Parade, "optocht", "Optocht"),
        (PhotoCategory.Dansgarde, "dansgarde", "Dansgarde"),
        (PhotoCategory.Youth, "jeugd", "Jeugd"),
        (PhotoCategory.Events, "evenementen", "Evenementen"),
        (PhotoCategory.Other, "overig", "Overig"),
    ];

    public static string Name(PhotoCategory category) => Categories.Single(c => c.Category == category).Name;

    public IReadOnlyList<AlbumCard> Albums { get; private set; } = [];

    public string? Filter { get; private set; }

    public async Task OnGetAsync(string? soort, CancellationToken cancellationToken)
    {
        (ActiveMenu, PageTitle) = ("fotos", "Foto's");
        MetaDescription = "Foto's van de pronkzitting, de optocht, carnaval en andere activiteiten van De Vrolijke Drammers.";
        var category = Categories.Where(c => c.Slug == soort).Select(c => (PhotoCategory?)c.Category).FirstOrDefault();
        Filter = category is null ? null : soort;
        Albums = await reader.AlbumsAsync(category, cancellationToken);
    }
}
