using Drammers.Modules.Content.Website;
using Drammers.Website.Content;

namespace Drammers.Website.Pages.Onderscheidingen;

public sealed class IndexModel(WebsiteReader reader) : SitePage
{
    public static readonly (AwardType Type, string Slug, string Name, string Text)[] Types =
    [
        (AwardType.Drammertje, "drammertje", "'t Drammertje", "Voor iemand die zich lang en met hart en ziel inzet voor de vereniging."),
        (AwardType.VerdienstelijkeDidammer, "verdienstelijke-didammer", "De Verdienstelijke Didammer", "Voor een inwoner die veel betekent voor de gemeenschap."),
        (AwardType.EikenloofVanBoschslag, "eikenloof-van-boschslag", "Het Eikenloof van Boschslag", "De hoogste onderscheiding van De Vrolijke Drammers."),
    ];

    public static string Name(AwardType type) => Types.Single(t => t.Type == type).Name;

    public IReadOnlyList<AwardCard> Awards { get; private set; } = [];

    public string? Filter { get; private set; }

    public async Task OnGetAsync(string? soort, CancellationToken cancellationToken)
    {
        (ActiveMenu, PageTitle) = ("vereniging", "Onderscheidingen");
        var type = Types.Where(t => t.Slug == soort).Select(t => (AwardType?)t.Type).FirstOrDefault();
        Filter = type is null ? null : soort;
        Awards = await reader.AwardsAsync(type, cancellationToken);
    }
}
