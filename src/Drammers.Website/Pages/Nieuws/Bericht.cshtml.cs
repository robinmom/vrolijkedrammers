using Drammers.Website.Content;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Website.Pages.Nieuws;

public sealed class BerichtModel(WebsiteReader reader) : SitePage
{
    public NewsArticle Article { get; private set; } = null!;

    public IReadOnlyList<NewsCard> More { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        ActiveMenu = "nieuws";
        var article = await reader.NewsArticleAsync(slug, cancellationToken);
        if (article is null)
        {
            return NotFoundPage();
        }

        Article = article;
        (PageTitle, MetaDescription, ShareImage) = (article.Card.Title, article.Card.Summary, article.Card.ImageUrl);
        More = [.. (await reader.NewsAsync(1, 4, cancellationToken)).Items.Where(n => n.Slug != slug).Take(3)];
        return Page();
    }
}
