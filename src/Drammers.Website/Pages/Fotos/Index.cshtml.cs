using Drammers.Modules.Content.Photos;
using Drammers.Website.Content;
using Microsoft.AspNetCore.Mvc;

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

    public SeasonNav Seasons { get; private set; } = null!;

    /// <summary>Albums van het actieve carnavalsjaar; oudere jaren via de knoppen eronder (fase 21g).</summary>
    public async Task<IActionResult> OnGetAsync(string? soort, string? seizoen, CancellationToken cancellationToken)
    {
        var calendar = await reader.SeasonsAsync(cancellationToken);
        var season = seizoen is null ? calendar.Current : calendar.Find(seizoen);
        if (season is null)
        {
            return NotFoundPage();
        }

        var category = Categories.Where(c => c.Slug == soort).Select(c => (PhotoCategory?)c.Category).FirstOrDefault();
        Filter = category is null ? null : soort;
        Seasons = new SeasonNav("/fotos", Filter is null ? null : $"soort={Filter}", calendar.Current, season,
            await reader.AlbumArchiveAsync(calendar, category, cancellationToken));
        (ActiveMenu, PageTitle) = ("fotos", Seasons.IsArchive ? $"Foto's {season.Slug}" : "Foto's");
        MetaDescription = "Foto's van de pronkzitting, de optocht, carnaval en andere activiteiten van De Vrolijke Drammers.";
        Albums = await reader.AlbumsAsync(category, season, cancellationToken);
        return Page();
    }

    /// <summary>Filterlink die het getoonde jaar vasthoudt.</summary>
    public string CategoryHref(string? slug)
    {
        var parts = new[] { slug is null ? null : $"soort={slug}", Seasons.IsArchive ? $"seizoen={Seasons.Shown.Slug}" : null }.Where(p => p is not null).ToList();
        return parts.Count == 0 ? "/fotos" : $"/fotos?{string.Join('&', parts)}";
    }
}
