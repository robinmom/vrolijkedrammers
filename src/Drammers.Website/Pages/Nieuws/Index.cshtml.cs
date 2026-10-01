using Drammers.Website.Content;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Website.Pages.Nieuws;

/// <summary>Nieuws van het actieve carnavalsjaar; oudere jaren via de knoppen eronder (<c>?seizoen=2025-2026</c>, fase 21g).</summary>
public sealed class IndexModel(WebsiteReader reader) : SitePage
{
    public const int PageSize = 12;

    public IReadOnlyList<NewsCard> Items { get; private set; } = [];

    public int Current { get; private set; }

    public int Pages { get; private set; }

    public SeasonNav Seasons { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int pagina, string? seizoen, CancellationToken cancellationToken)
    {
        var calendar = await reader.SeasonsAsync(cancellationToken);
        var season = seizoen is null ? calendar.Current : calendar.Find(seizoen);
        if (season is null)
        {
            return NotFoundPage();
        }

        Seasons = new SeasonNav("/nieuws", null, calendar.Current, season, await reader.NewsArchiveAsync(calendar, cancellationToken));
        (ActiveMenu, PageTitle) = ("nieuws", Seasons.IsArchive ? $"Nieuws {season.Slug}" : "Nieuws");
        Current = Math.Max(1, pagina);
        var (items, total) = await reader.NewsAsync(season, Current, PageSize, cancellationToken);
        (Items, Pages) = (items, Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)));
        return Page();
    }

    public string PageHref(int page)
    {
        var href = Seasons.Href(Seasons.Shown);
        return page <= 1 ? href : $"{href}{(href.Contains('?') ? '&' : '?')}pagina={page}";
    }
}
