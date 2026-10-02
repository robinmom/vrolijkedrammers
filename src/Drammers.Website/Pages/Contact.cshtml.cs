using Drammers.Website.Content;

namespace Drammers.Website.Pages;

public sealed class ContactModel(WebsiteReader reader, SiteShell shell) : SitePage
{
    public Modules.Content.Website.WebsiteSettings Settings { get; private set; } = null!;

    public PageContent? Extra { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        (ActiveMenu, PageTitle) = ("contact", "Contact");
        Scripts = ["/_content/Drammers.Website/js/forms/contact.js"];
        Settings = await shell.SettingsAsync();
        Extra = await reader.PageAsync("contact", cancellationToken);
    }
}
