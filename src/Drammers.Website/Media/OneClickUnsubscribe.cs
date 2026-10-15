using Drammers.Infrastructure.Mailings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Drammers.Website.Media;

/// <summary>
/// Afmelden met één klik vanuit het mailprogramma (RFC 8058, kop List-Unsubscribe-Post): Outlook en Gmail doen zelf een
/// POST naar dit adres, zonder pagina of knop. Opent iemand het adres in de browser, dan komt die op de gewone
/// afmeldpagina met de knop (een virusscanner die links opent, meldt zo niemand af).
/// </summary>
public static class OneClickUnsubscribe
{
    public static IEndpointRouteBuilder MapOneClickUnsubscribe(this IEndpointRouteBuilder app)
    {
        app.MapPost(MailingService.OneClickUnsubscribePath, UnsubscribeAsync).AllowAnonymous().ExcludeFromDescription();
        app.MapGet(MailingService.OneClickUnsubscribePath, ([FromQuery] string? t) =>
            Results.Redirect(t is null ? "/afmelden" : $"/afmelden?t={Uri.EscapeDataString(t)}")).AllowAnonymous().ExcludeFromDescription();
        return app;
    }

    private static async Task<IResult> UnsubscribeAsync([FromQuery] string? t, [FromServices] MailingService mailings, CancellationToken cancellationToken) =>
        await mailings.UnsubscribeAsync(t, cancellationToken) is null ? Results.BadRequest() : Results.Ok();
}
