using Drammers.Website.Content;

namespace Drammers.Website.Pages;

public sealed class IndexModel(WebsiteReader reader, InstagramFeed instagram, SiteShell shell) : SitePage
{
    public Hero Hero { get; private set; } = null!;

    public IReadOnlyList<EventCard> Events { get; private set; } = [];

    public IReadOnlyList<NewsCard> News { get; private set; } = [];

    public IReadOnlyList<InstagramPost> Instagram { get; private set; } = [];

    public string? InstagramUrl { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ActiveMenu = "home";
        Hero = await reader.HeroAsync(cancellationToken);
        ShareImage = Hero.ImageUrl;
        Events = await reader.EventsAsync(3, null, cancellationToken);
        News = (await reader.NewsAsync((await reader.SeasonsAsync(cancellationToken)).Current, 1, 5, cancellationToken)).Items;
        Instagram = await instagram.LatestAsync(cancellationToken);
        InstagramUrl = (await shell.SettingsAsync()).InstagramUrl;
    }
}
