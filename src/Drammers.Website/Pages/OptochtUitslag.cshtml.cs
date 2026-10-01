using Drammers.Infrastructure.ParadeManagement;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Website.Pages;

/// <summary>Uitslag van de optocht (fase 22c): pas zichtbaar nadat de uitslagcommissie hem na de prijsuitreiking publiceert.</summary>
public sealed class OptochtUitslagModel(ParadeResults results) : SitePage
{
    public ParadeResultOverview? Results { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Results = await results.PublishedAsync(cancellationToken);
        if (Results is null)
        {
            return NotFoundPage();
        }

        (ActiveMenu, PageTitle) = ("optocht", $"Uitslag {Results.ParadeName}");
        MetaDescription = $"De uitslag van {Results.ParadeName} per categorie.";
        return Page();
    }
}
