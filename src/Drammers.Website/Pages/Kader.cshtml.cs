using Drammers.Website.Content;

namespace Drammers.Website.Pages;

public sealed class KaderModel(WebsiteReader reader) : SitePage
{
    public IReadOnlyList<CommitteeView> Committees { get; private set; } = [];

    public CommitteeView? Active { get; private set; }

    public async Task OnGetAsync(string? commissie, CancellationToken cancellationToken)
    {
        (ActiveMenu, PageTitle) = ("vereniging", "Het kader");
        MetaDescription = "Bestuur, Raad van Elf, convent en de leiding van de dansgarde van De Vrolijke Drammers.";
        Committees = await reader.KaderAsync(cancellationToken);
        Active = Committees.FirstOrDefault(c => c.Slug == commissie) ?? Committees.FirstOrDefault();
    }
}
