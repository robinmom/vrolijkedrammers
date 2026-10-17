using Drammers.Infrastructure.Mailings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Drammers.Website.Pages;

/// <summary>
/// Afmelden voor nieuwsbrieven en uitnodigingen (fase 27a) via de link onder een mailing. Eerst een knop: zo meldt een
/// virusscanner die de link opent niemand per ongeluk af. Nooit in de output cache: de pagina bevat een
/// antiforgery-token (bij de cookie van één bezoeker) en een deels verborgen e-mailadres.
/// </summary>
[OutputCache(NoStore = true)]
public sealed class AfmeldenModel(MailingService mailings, MailingUnsubscribeTokens tokens) : SitePage
{
    [BindProperty(SupportsGet = true, Name = "t")]
    public string? Token { get; set; }

    /// <summary>Het adres, deels verborgen (bijvoorbeeld p***@example.com).</summary>
    public string? MaskedEmail { get; private set; }

    public bool Done { get; private set; }

    public void OnGet()
    {
        // Ook de website-cachepolicy slaat een response met no-store over (RespectNoStorePolicy).
        Response.Headers.CacheControl = "no-store";
        (PageTitle, NoIndex) = ("Afmelden", true);
        MaskedEmail = Mask(tokens.Read(Token));
    }

    public async Task OnPostAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        (PageTitle, NoIndex) = ("Afmelden", true);
        var address = await mailings.UnsubscribeAsync(Token, cancellationToken);
        (MaskedEmail, Done) = (Mask(address), address is not null);
    }

    private static string? Mask(string? email)
    {
        if (email is null || email.IndexOf('@') is var at && at < 1)
        {
            return null;
        }

        return $"{email[0]}***{email[at..]}";
    }
}
