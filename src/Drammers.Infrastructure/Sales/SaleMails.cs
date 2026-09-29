using System.Globalization;
using System.Net;
using Drammers.Infrastructure.Email;
using Drammers.Modules.Ticketing.Sales;

namespace Drammers.Infrastructure.Sales;

/// <summary>E-mails van de kaartverkoop (fase 19): bevestiging met de link naar de QR, betaallink en wachtlijst.</summary>
public static class SaleMails
{
    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    public static string What(SaleOrder order, SaleProduct product) => product.Kind == SaleProductKind.Tokens
        ? $"{order.Quantity} munten"
        : $"{order.Quantity} kaart{(order.Quantity == 1 ? "" : "en")}";

    private static string When(SaleProduct product) =>
        product.Date is { } d ? $" op {d.ToString("dddd d MMMM yyyy", Dutch)}" : "";

    public static EmailMessage Confirmation(SaleOrder order, SaleProduct product, string link)
    {
        var what = What(order, product);
        var collect = product.Kind == SaleProductKind.Tokens
            ? "Haal je munten zelf op bij de kassa met de munten-QR in de app (op het beginscherm). De aankoop is persoonsgebonden."
            : "Laat de QR scannen bij de ingang. Eén QR geldt voor alle kaarten van deze bestelling: bij het scannen gaan alle personen tegelijk naar binnen.";
        var subject = $"Je bestelling {order.Number}: {product.Name}";
        var text = $"""
            Beste {order.BuyerName},

            Bedankt voor je bestelling {order.Number}: {what} voor {product.Name}{When(product)}.
            {collect}

            Bekijk je bestelling en de QR: {link}
            Bewaar deze e-mail. Kaarten en munten worden niet terugbetaald.

            Groeten,
            De Vrolijke Drammers
            """;
        var html = $"<p>Beste {H(order.BuyerName)},</p><p>Bedankt voor je bestelling <strong>{H(order.Number)}</strong>: {H(what)} voor <strong>{H(product.Name)}</strong>{H(When(product))}.</p><p>{H(collect)}</p><p><a href=\"{H(link)}\">Bekijk je bestelling en de QR</a> (bewaar deze e-mail).</p><p>Kaarten en munten worden niet terugbetaald.</p><p>Groeten,<br>De Vrolijke Drammers</p>";
        return new EmailMessage(order.BuyerEmail, subject, text, html);
    }

    public static EmailMessage PaymentLink(SaleOrder order, SaleProduct product, string link, bool invitedFromWaitlist)
    {
        var what = What(order, product);
        var until = order.HoldUntil is { } h
            ? TimeZoneInfo.ConvertTimeFromUtc(h, TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam")).ToString("dddd d MMMM 'om' HH:mm", Dutch)
            : "binnenkort";
        var intro = invitedFromWaitlist
            ? $"Goed nieuws: er is plek voor je van de wachtlijst. We houden {what} voor {product.Name}{When(product)} voor je vast."
            : $"Voor je bestelling {order.Number} staan {what} voor {product.Name}{When(product)} klaar.";
        var subject = invitedFromWaitlist ? $"Er is plek: {product.Name}" : $"Betaallink voor je bestelling {order.Number}";
        var text = $"""
            Beste {order.BuyerName},

            {intro}
            Te betalen: {TicketSales.Euro(order.AmountCents)}. Betaal uiterlijk {until} met iDEAL via deze link: {link}
            Daarna vervalt de link en gaan de plaatsen naar iemand anders.

            Groeten,
            De Vrolijke Drammers
            """;
        var html = $"<p>Beste {H(order.BuyerName)},</p><p>{H(intro)}</p><p>Te betalen: <strong>{H(TicketSales.Euro(order.AmountCents))}</strong>. Betaal uiterlijk <strong>{H(until)}</strong> met iDEAL.</p><p><a href=\"{H(link)}\" style=\"display:inline-block;padding:12px 20px;background:#ED0012;color:#ffffff;border-radius:24px;text-decoration:none;font-weight:bold\">Betalen</a></p><p>Daarna vervalt de link en gaan de plaatsen naar iemand anders. Kaarten worden niet terugbetaald.</p><p>Groeten,<br>De Vrolijke Drammers</p>";
        return new EmailMessage(order.BuyerEmail, subject, text, html);
    }

    public static EmailMessage WaitlistJoined(WaitlistEntry entry, SaleProduct product)
    {
        var what = $"{entry.Quantity} kaart{(entry.Quantity == 1 ? "" : "en")}";
        var subject = $"Je staat op de wachtlijst: {product.Name}";
        var text = $"""
            Beste {entry.BuyerName},

            Je staat op de wachtlijst voor {product.Name}{When(product)} ({what}). Komt er plek, dan krijg je een e-mail{(entry.BuyerUserId is null ? "" : " en een melding in de app")}.
            Voor betaalde kaarten zit daar een betaallink bij die {WaitlistEntry.InviteHours} uur geldig is.

            Groeten,
            De Vrolijke Drammers
            """;
        var html = $"<p>Beste {H(entry.BuyerName)},</p><p>Je staat op de wachtlijst voor <strong>{H(product.Name)}</strong>{H(When(product))} ({H(what)}). Komt er plek, dan krijg je een e-mail{(entry.BuyerUserId is null ? "" : " en een melding in de app")}.</p><p>Voor betaalde kaarten zit daar een betaallink bij die {WaitlistEntry.InviteHours} uur geldig is.</p><p>Groeten,<br>De Vrolijke Drammers</p>";
        return new EmailMessage(entry.BuyerEmail, subject, text, html);
    }

    private static string H(string value) => WebUtility.HtmlEncode(value);
}
