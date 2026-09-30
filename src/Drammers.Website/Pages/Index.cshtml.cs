using Drammers.Website.Content;

namespace Drammers.Website.Pages;

public sealed class IndexModel(WebsiteReader reader, FacebookFeed facebook, SiteShell shell) : SitePage
{
    public Hero Hero { get; private set; } = null!;

    public IReadOnlyList<EventCard> Events { get; private set; } = [];

    public IReadOnlyList<NewsCard> News { get; private set; } = [];

    public IReadOnlyList<FacebookPost> Facebook { get; private set; } = [];

    public string? FacebookPageUrl { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ActiveMenu = "home";
        Hero = await reader.HeroAsync(cancellationToken);
        ShareImage = Hero.ImageUrl;
        Events = await reader.EventsAsync(3, null, cancellationToken);
        News = (await reader.NewsAsync(1, 4, cancellationToken)).Items;
        Facebook = await facebook.LatestAsync(cancellationToken);
        FacebookPageUrl = (await shell.SettingsAsync()).FacebookPageUrl;
    }
}
