using Drammers.Infrastructure.Mailings;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Website.Pages;

/// <summary>
/// Afmelden voor nieuwsbrieven en uitnodigingen (fase 27a) via de link onder een mailing. Eerst een knop: zo meldt een
/// virusscanner die de link opent niemand per ongeluk af.
/// </summary>
public sealed class AfmeldenModel(MailingService mailings, MailingUnsubscribeTokens tokens) : SitePage
{
    [BindProperty(SupportsGet = true, Name = "t")]
    public string? Token { get; set; }

    /// <summary>Het adres, deels verborgen (bijvoorbeeld p***@example.com).</summary>
    public string? MaskedEmail { get; private set; }

    public bool Done { get; private set; }

    public void OnGet()
    {
        (PageTitle, NoIndex) = ("Afmelden", true);
        MaskedEmail = Mask(tokens.Read(Token));
    }

    public async Task OnPostAsync(CancellationToken cancellationToken)
    {
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
