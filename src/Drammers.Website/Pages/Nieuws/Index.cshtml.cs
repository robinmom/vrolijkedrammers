using Drammers.Website.Content;

namespace Drammers.Website.Pages.Nieuws;

public sealed class IndexModel(WebsiteReader reader) : SitePage
{
    public const int PageSize = 12;

    public IReadOnlyList<NewsCard> Items { get; private set; } = [];

    public int Current { get; private set; }

    public int Pages { get; private set; }

    public async Task OnGetAsync(int pagina, CancellationToken cancellationToken)
    {
        (ActiveMenu, PageTitle) = ("nieuws", "Nieuws");
        Current = Math.Max(1, pagina);
        var (items, total) = await reader.NewsAsync(Current, PageSize, cancellationToken);
        (Items, Pages) = (items, Math.Max(1, (int)Math.Ceiling(total / (double)PageSize)));
    }
}
