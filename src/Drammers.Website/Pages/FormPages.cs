using Microsoft.AspNetCore.Mvc;

namespace Drammers.Website.Pages;

/// <summary>
/// De onderdelen die eerst losse pagina's waren (lid worden, optocht inschrijven, aanrijtijden en kaarten) zijn sinds
/// fase 21d gewone websitepagina's met kop, menu en voet. Het formulier zelf staat in <c>Shared/Forms</c> als pure HTML
/// (geen Razor), zodat de end-to-endtests van de portal precies dezelfde HTML met het script kunnen testen.
/// </summary>
public abstract class FormPage : SitePage
{
    protected static string Script(string name) => $"/_content/Drammers.Website/js/forms/{name}.js";
}

public sealed class LidWordenModel : FormPage
{
    public void OnGet()
    {
        (ActiveMenu, PageTitle) = ("", "Lid worden");
        MetaDescription = "Meld je aan als lid van carnavalsvereniging De Vrolijke Drammers uit Loil.";
        Scripts = [Script("lid-worden")];
    }
}

public sealed class OptochtInschrijvenModel : FormPage
{
    public void OnGet()
    {
        (ActiveMenu, PageTitle) = ("optocht", "Optocht inschrijven");
        MetaDescription = "Schrijf je groep of praalwagen in voor de carnavalsoptocht van De Vrolijke Drammers in Loil.";
        // Inloggen (MSAL) vóór het formulierscript, zodat de pagina weet of je bent ingelogd.
        Scripts = ["/_content/Drammers.Website/js/vendor/msal-browser.min.js", "/_content/Drammers.Website/js/login.js", Script("optocht-inschrijven")];
    }
}

public sealed class AanrijtijdenModel : FormPage
{
    public void OnGet()
    {
        (ActiveMenu, PageTitle) = ("optocht", "Aanrijtijden optocht");
        MetaDescription = "Aanrijtijden van de wagens voor de carnavalsoptocht van De Vrolijke Drammers in Loil.";
        Scripts = [Script("aanrijtijden")];
    }
}

public sealed class KaartenModel(SiteShell shell) : FormPage
{
    public async Task<IActionResult> OnGetAsync()
    {
        // Tot de kaartverkoop open is (feature flag website.kaarten) bestaat de pagina niet.
        if (!await shell.ShowTicketsAsync())
        {
            return NotFoundPage();
        }

        (ActiveMenu, PageTitle) = ("", "Kaarten");
        MetaDescription = "Koop kaarten voor de pronkzitting, dagkaarten voor carnaval en kaarten voor activiteiten van De Vrolijke Drammers.";
        Scripts = [Script("kaarten")];
        return Page();
    }
}

public sealed class KaartenBestellingModel : FormPage
{
    public void OnGet()
    {
        (ActiveMenu, PageTitle) = ("", "Je bestelling");
        // Persoonlijke link met token: niet in zoekmachines.
        NoIndex = true;
        Scripts = [Script("kaarten-bestelling")];
    }
}
