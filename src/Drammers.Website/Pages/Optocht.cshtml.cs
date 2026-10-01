using Drammers.Website.Content;

namespace Drammers.Website.Pages;

public sealed class OptochtModel(WebsiteReader reader) : SitePage
{
    public ParadeView? Parade { get; private set; }

    public PageContent? Extra { get; private set; }

    /// <summary>Naam van de optocht met een gepubliceerde uitslag (fase 22c).</summary>
    public string? Results { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        (ActiveMenu, PageTitle) = ("optocht", "Optocht");
        MetaDescription = "De optocht van De Vrolijke Drammers in Loil: inschrijven, aanrijtijden en praktische informatie.";
        Parade = await reader.ParadeAsync(cancellationToken);
        Extra = await reader.PageAsync("optocht", cancellationToken);
        Results = await reader.PublishedResultsAsync(cancellationToken);
    }
}
