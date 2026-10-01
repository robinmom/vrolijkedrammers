using Drammers.Website.Content;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Website.Pages.Onderscheidingen;

public sealed class DetailModel(WebsiteReader reader) : SitePage
{
    public AwardCard Award { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        ActiveMenu = "vereniging";
        var award = await reader.AwardAsync(slug, cancellationToken);
        if (award is null)
        {
            return NotFoundPage();
        }

        Award = award;
        (PageTitle, MetaDescription, ShareImage) = ($"{IndexModel.Name(award.Type)} {award.Year}: {award.Recipient}", award.Excerpt, award.PhotoUrl);
        return Page();
    }
}
