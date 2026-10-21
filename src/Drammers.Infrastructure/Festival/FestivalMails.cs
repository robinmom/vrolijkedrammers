using System.Globalization;
using System.Net;
using System.Text;
using Drammers.Infrastructure.Email;
using Drammers.Modules.Content.Festival;

namespace Drammers.Infrastructure.Festival;

/// <summary>Mails van het Dansgardefestival: bevestiging, melding aan de organisatie, beslissing en betaallink.</summary>
public static class FestivalMails
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public static string TypeLabel(DanceType type) => type == DanceType.Garde ? "Gardedans" : "Showdans";

    public static string FormatLabel(DanceFormat format) => format switch { DanceFormat.Solo => "Solo", DanceFormat.Duo => "Duo", _ => "Groep" };

    public static string StyleLabel(ShowStyle style) => style == ShowStyle.Character ? "Karakter" : "Modern";

    public static string StatusLabel(DanceEntryStatus status) => status switch
    {
        DanceEntryStatus.New => "Nieuw",
        DanceEntryStatus.InfoRequested => "Meer informatie gevraagd",
        DanceEntryStatus.Approved => "Goedgekeurd",
        DanceEntryStatus.Waitlisted => "Wachtlijst",
        DanceEntryStatus.Rejected => "Afgewezen",
        _ => "Gewijzigd, wordt opnieuw beoordeeld",
    };

    public static string Euro(int cents) => (cents / 100m).ToString("C", Nl);

    public static string DanceLine(DanceFestivalEntry e) =>
        $"{TypeLabel(e.DanceType)} · {FormatLabel(e.Format)} · {e.Category} — {e.Names}"
        + (e.Title is null ? "" : $" — “{e.Title}”{(e.ShowStyle is { } s ? $" ({StyleLabel(s)})" : "")}")
        + $" · {e.DancerCount} danser{(e.DancerCount == 1 ? "" : "s")} · gem. {e.AverageAge.ToString("0.0", Nl)} jaar · leiding {e.LeaderName}";

    public static EmailMessage Confirmation(DanceFestival festival, DanceFestivalRegistration registration, string editLink, int priceCents)
    {
        var dances = registration.Entries.OrderBy(e => e.SortOrder).Select(DanceLine).ToList();
        var subject = $"Inschrijving {festival.Name}: nummer {registration.Number}";
        var text = new StringBuilder()
            .AppendLine($"Beste {registration.ContactName},").AppendLine()
            .AppendLine($"Bedankt voor de inschrijving van {registration.ClubName} voor het {festival.Name} op {festival.FestivalDate.ToString("dddd d MMMM yyyy", Nl)}.")
            .AppendLine($"Uw inschrijfnummer is {registration.Number}. Plaatsing gaat op volgorde van inschrijfnummer; vol = vol.").AppendLine()
            .AppendLine("Uw dansen:");
        foreach (var d in dances)
        {
            text.AppendLine($"- {d}");
        }

        text.AppendLine()
            .AppendLine($"Inschrijfgeld volgens de tarieven: {Euro(priceCents)}. U betaalt pas na goedkeuring; dan ontvangt u een betaallink.")
            .AppendLine($"Foto's en video's van uw optredens: {(registration.PhotoConsent ? "toestemming gegeven" : "geen toestemming")}.").AppendLine()
            .AppendLine("Tot de beoordeling kunt u de inschrijving nog wijzigen via deze link:")
            .AppendLine(editLink).AppendLine()
            .AppendLine("Met vriendelijke groet,").AppendLine("De organisatie van het Dansgardefestival, De Vrolijke Drammers");
        var html = $"""
            <p>Beste {E(registration.ContactName)},</p>
            <p>Bedankt voor de inschrijving van <strong>{E(registration.ClubName)}</strong> voor het {E(festival.Name)} op {E(festival.FestivalDate.ToString("dddd d MMMM yyyy", Nl))}.</p>
            <p>Uw inschrijfnummer is <strong>{registration.Number}</strong>. Plaatsing gaat op volgorde van inschrijfnummer; vol = vol.</p>
            <p><strong>Uw dansen</strong></p>
            <ul>{string.Concat(dances.Select(d => $"<li>{E(d)}</li>"))}</ul>
            <p>Inschrijfgeld volgens de tarieven: <strong>{Euro(priceCents)}</strong>. U betaalt pas na goedkeuring; dan ontvangt u een betaallink.</p>
            <p>Foto's en video's van uw optredens: {(registration.PhotoConsent ? "toestemming gegeven" : "geen toestemming")}.</p>
            <p>Tot de beoordeling kunt u de inschrijving nog wijzigen: <a href="{E(editLink)}">inschrijving bekijken of wijzigen</a>.</p>
            <p>Met vriendelijke groet,<br>De organisatie van het Dansgardefestival, De Vrolijke Drammers</p>
            """;
        return Message(festival, registration.Email, subject, text.ToString(), html);
    }

    public static EmailMessage NewRegistration(DanceFestival festival, DanceFestivalRegistration registration, bool changed)
    {
        var subject = changed
            ? $"Inschrijving {registration.Number} gewijzigd: {registration.ClubName}"
            : $"Nieuwe inschrijving {registration.Number}: {registration.ClubName}";
        var dances = registration.Entries.Where(e => e.RemovedAt is null).OrderBy(e => e.SortOrder).Select(DanceLine).ToList();
        var text = $"""
            {(changed ? "Een vereniging heeft haar inschrijving gewijzigd" : "Er is een nieuwe inschrijving")} voor het {festival.Name}.

            {registration.Number}. {registration.ClubName} — {registration.ContactName}, {registration.Phone}, {registration.Email}
            {string.Join(Environment.NewLine, dances.Select(d => "- " + d))}

            Beoordelen in het portal: Dansgarde → Dansgardefestival.
            """;
        var html = $"""
            <p>{(changed ? "Een vereniging heeft haar inschrijving gewijzigd" : "Er is een nieuwe inschrijving")} voor het {E(festival.Name)}.</p>
            <p><strong>{registration.Number}. {E(registration.ClubName)}</strong> — {E(registration.ContactName)}, {E(registration.Phone)}, {E(registration.Email)}</p>
            <ul>{string.Concat(dances.Select(d => $"<li>{E(d)}</li>"))}</ul>
            <p>Beoordelen in het portal: Dansgarde → Dansgardefestival.</p>
            """;
        return new EmailMessage(festival.NotificationEmail, subject, text, html, ReplyTo: registration.Email, From: DanceFestivals.SenderLocalPart);
    }

    public static EmailMessage Decision(
        DanceFestival festival, DanceFestivalRegistration registration, IReadOnlyList<DanceFestivalEntry> entries, FestivalDecision decision, string? message,
        string editLink)
    {
        var (subject, intro) = decision switch
        {
            FestivalDecision.Approve => ("goedgekeurd", "Goed nieuws: de volgende dansen zijn goedgekeurd en hebben een plek op het festival."),
            FestivalDecision.RequestInfo => ("meer informatie nodig", "Voor de volgende dansen hebben we meer informatie nodig."),
            FestivalDecision.Waitlist => ("op de wachtlijst", "Het festival is vol. De volgende dansen staan op de wachtlijst; komt er een plek vrij, dan hoort u het van ons."),
            _ => ("niet geplaatst", "Helaas kunnen de volgende dansen niet worden geplaatst."),
        };
        var lines = entries.OrderBy(e => e.SortOrder).Select(DanceLine).ToList();
        var text = new StringBuilder()
            .AppendLine($"Beste {registration.ContactName},").AppendLine()
            .AppendLine(intro).AppendLine();
        foreach (var l in lines)
        {
            text.AppendLine($"- {l}");
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            text.AppendLine().AppendLine(message);
        }

        var showLink = decision is FestivalDecision.RequestInfo;
        if (showLink)
        {
            text.AppendLine().AppendLine("Pas uw inschrijving aan via deze link:").AppendLine(editLink);
        }

        text.AppendLine().AppendLine("Met vriendelijke groet,").AppendLine("De organisatie van het Dansgardefestival, De Vrolijke Drammers");
        var html = $"""
            <p>Beste {E(registration.ContactName)},</p>
            <p>{E(intro)}</p>
            <ul>{string.Concat(lines.Select(l => $"<li>{E(l)}</li>"))}</ul>
            {(string.IsNullOrWhiteSpace(message) ? "" : $"<p style=\"white-space:pre-line\">{E(message)}</p>")}
            {(showLink ? $"<p><a href=\"{E(editLink)}\">Inschrijving aanpassen</a></p>" : "")}
            <p>Met vriendelijke groet,<br>De organisatie van het Dansgardefestival, De Vrolijke Drammers</p>
            """;
        return Message(festival, registration.Email, $"{festival.Name}: inschrijving {registration.Number} {subject}", text.ToString(), html);
    }

    public static EmailMessage PaymentLink(
        DanceFestival festival, DanceFestivalRegistration registration, int amountCents, string payLink, EmailFile? invoice = null)
    {
        var text = $"""
            Beste {registration.ContactName},

            Het inschrijfgeld voor het {festival.Name} (inschrijving {registration.Number}, {registration.ClubName}) is {Euro(amountCents)}.
            U kunt betalen met iDEAL via deze link:
            {payLink}

            In de bijlage vindt u de factuur.

            Met vriendelijke groet,
            De organisatie van het Dansgardefestival, De Vrolijke Drammers
            """;
        var html = $"""
            <p>Beste {E(registration.ContactName)},</p>
            <p>Het inschrijfgeld voor het {E(festival.Name)} (inschrijving {registration.Number}, {E(registration.ClubName)}) is <strong>{Euro(amountCents)}</strong>.</p>
            <p><a href="{E(payLink)}" style="display:inline-block;padding:10px 20px;background:#d4000f;color:#fff;border-radius:999px;text-decoration:none">Betalen met iDEAL</a></p>
            <p>In de bijlage vindt u de factuur.</p>
            <p>Met vriendelijke groet,<br>De organisatie van het Dansgardefestival, De Vrolijke Drammers</p>
            """;
        return Message(festival, registration.Email, $"{festival.Name}: betaallink inschrijving {registration.Number}", text, html, invoice);
    }

    private static EmailMessage Message(DanceFestival festival, string to, string subject, string text, string html, EmailFile? attachment = null) =>
        new(to, subject, text, html, ReplyTo: festival.NotificationEmail, From: DanceFestivals.SenderLocalPart,
            Attachments: attachment is null ? null : [attachment]);

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
