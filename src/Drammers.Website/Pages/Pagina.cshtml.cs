using Drammers.Website.Content;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Website.Pages;

/// <summary>Vaste pagina's uit het portal (Website → Pagina's) op hun eigen webadres, bijvoorbeeld <c>/over-ons</c>.</summary>
public sealed class PaginaModel(WebsiteReader reader) : SitePage
{
    public PageContent Text { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        var page = await reader.PageAsync(slug, cancellationToken);
        if (page is null)
        {
            return NotFoundPage();
        }

        Text = page;
        ActiveMenu = WebsiteReader.AssociationPages.Contains(slug) ? "vereniging" : "";
        (PageTitle, MetaDescription, ShareImage) = (page.Title, page.Intro, page.ImageUrl);
        return Page();
    }
}
