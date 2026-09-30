using Drammers.Modules.Content.Events;
using Drammers.Website.Content;

namespace Drammers.Website.Pages;

public sealed class AgendaModel(WebsiteReader reader) : SitePage
{
    public IReadOnlyList<EventCard> Events { get; private set; } = [];

    public IReadOnlyList<EventCategory> Categories { get; private set; } = [];

    public string? Category { get; private set; }

    public async Task OnGetAsync(string? categorie, CancellationToken cancellationToken)
    {
        (ActiveMenu, PageTitle) = ("agenda", "Agenda");
        MetaDescription = "Alle openbare activiteiten van carnavalsvereniging De Vrolijke Drammers in Loil.";
        Categories = await reader.EventCategoriesAsync(cancellationToken);
        Category = Categories.Any(c => c.Code == categorie) ? categorie : null;
        Events = await reader.EventsAsync(null, Category, cancellationToken);
    }
}
