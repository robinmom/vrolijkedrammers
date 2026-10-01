namespace Drammers.Website.Pages;

public sealed class DoeMeeModel : SitePage
{
    public void OnGet() => (ActiveMenu, PageTitle) = ("", "Doe mee");
}
