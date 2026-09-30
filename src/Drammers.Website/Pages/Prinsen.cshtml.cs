using Drammers.Modules.Content.Website;
using Drammers.Website.Content;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Website.Pages;

/// <summary>
/// Prinsengalerie en (apart, pas zichtbaar als het bestuur hem aanzet) de jeugdprinsen. Klik op een prins opent de
/// details (jaar, prinsennaam, naam, motto) als venster; dat werkt ook zonder JavaScript (<c>?prins=…</c>).
/// </summary>
public sealed class PrinsenModel(WebsiteReader reader, SiteShell shell) : SitePage
{
    public IReadOnlyList<PrinceCard> Princes { get; private set; } = [];

    public PrinceCard? Selected { get; private set; }

    public PrinceCard? Previous { get; private set; }

    public PrinceCard? Next { get; private set; }

    public bool Youth { get; private set; }

    public string BasePath => Youth ? "/jeugdprinsen" : "/prinsengalerie";

    public async Task<IActionResult> OnGetAsync(Guid? prins, CancellationToken cancellationToken)
    {
        ActiveMenu = "vereniging";
        Youth = HttpContext.Request.Path.StartsWithSegments("/jeugdprinsen");
        if (Youth && !(await shell.SettingsAsync()).ShowYouthPrinces)
        {
            return NotFoundPage();
        }

        PageTitle = Youth ? "Jeugdprinsen" : "Prinsengalerie";
        Princes = await reader.PrincesAsync(Youth ? PrinceKind.YouthPrince : PrinceKind.Prince, cancellationToken);
        var index = prins is null ? -1 : Princes.ToList().FindIndex(p => p.Id == prins);
        if (index >= 0)
        {
            Selected = Princes[index];
            Previous = index > 0 ? Princes[index - 1] : null;
            Next = index < Princes.Count - 1 ? Princes[index + 1] : null;
            (PageTitle, ShareImage) = ($"{Selected.PrinceName} ({Selected.Year})", Selected.PhotoUrl);
        }

        return Page();
    }
}
