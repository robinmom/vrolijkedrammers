using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.Website.Pages;

/// <summary>Basis voor alle websitepagina's: titel, omschrijving, actief menu-item en een nette 404.</summary>
public abstract class SitePage : PageModel
{
    /// <summary>Titel in de tab en in zoekmachines (zonder de naam van de vereniging).</summary>
    public string PageTitle { get; protected set; } = "";

    public string? MetaDescription { get; protected set; }

    /// <summary>Menu-item dat rood onderstreept is: home, agenda, nieuws, vereniging, optocht, fotos, contact of doe-mee.</summary>
    public string ActiveMenu { get; protected set; } = "";

    /// <summary>Afbeelding voor delen op social media (Open Graph).</summary>
    public string? ShareImage { get; protected set; }

    public bool IsNotFound { get; private set; }

    /// <summary>Extra scripts voor deze pagina (fase 21d, bijvoorbeeld een formulier), na <c>site.js</c> en met <c>defer</c>.</summary>
    public IReadOnlyList<string> Scripts { get; protected set; } = [];

    /// <summary>Niet opnemen in zoekmachines (bijvoorbeeld een bestelling met een persoonlijke link).</summary>
    public bool NoIndex { get; protected set; }

    /// <summary>
    /// Een oud adres van de WordPress-site stuurt permanent door naar de nieuwe pagina; anders de 404 in de huisstijl.
    /// </summary>
    protected async Task<IActionResult> NotFoundOrRedirectAsync(string? path = null)
    {
        var redirects = HttpContext.RequestServices.GetRequiredService<Content.Redirects>();
        var target = await redirects.FindAsync(path ?? Request.Path.Value ?? "/", HttpContext.RequestAborted);
        return target is not null && target != Request.Path.Value ? RedirectPermanent(target) : NotFoundPage();
    }

    /// <summary>Toont de 404-pagina in de huisstijl (met status 404, zodat zoekmachines hem niet opnemen).</summary>
    protected IActionResult NotFoundPage()
    {
        IsNotFound = true;
        PageTitle = "Pagina niet gevonden";
        Response.StatusCode = StatusCodes.Status404NotFound;
        return Page();
    }
}

/// <summary>Nederlandse datums en tijden in de tijdzone van Loil.</summary>
public static class Nl
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("nl-NL");

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>Bijvoorbeeld "17 februari 2026".</summary>
    public static string Date(DateTime utc) => Local(utc).ToString("d MMMM yyyy", Culture);

    public static string Date(DateOnly date) => date.ToString("d MMMM yyyy", Culture);

    public static string Time(DateTime utc) => Local(utc).ToString("HH:mm", Culture);

    public static string Weekday(DateTime utc) => Local(utc).ToString("ddd", Culture).TrimEnd('.').ToUpperInvariant();

    public static string Month(DateTime utc) => Local(utc).ToString("MMM", Culture).TrimEnd('.').ToUpperInvariant();

    public static string MonthYear(DateTime utc)
    {
        var text = Local(utc).ToString("MMMM yyyy", Culture);
        return char.ToUpper(text[0], Culture) + text[1..];
    }
}
