using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Festival;
using Drammers.Modules.Content.Shared;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Festival;

public sealed record FestivalPublishStatus(Guid? NewsId, string? NewsSlug, DateTime? ReportsSentAt, int ReportsSent, int Registrations);

/// <summary>
/// Na de prijsuitreiking (2026-10-15): de uitslag als nieuwsbericht op de website (plaatsen en punten per categorie) en het
/// juryrapport per vereniging (PDF per e-mail van dansgardefestival@). Beide kan pas als de prijsuitreiking is afgerond.
/// </summary>
public sealed class FestivalResultsPublisher(
    DrammersDbContext db,
    FestivalProgramService program,
    FestivalJudgingService judging,
    ContentAdministration content,
    IOutbox outbox,
    IAuditLogger audit,
    IClock clock)
{
    public const string ReportMailMessageType = "festival.report-mail";

    /// <summary>Afzender van het rapport; bestaat als mailbox (gedeeld) in Microsoft 365 en als afzender in ACS.</summary>
    public const string Sender = "dansgardefestival";

    public const string SenderAddress = "dansgardefestival@vrolijkedrammers.nl";

    public sealed record ReportMail(Guid RegistrationId);

    public async Task<FestivalPublishStatus> StatusAsync(CancellationToken cancellationToken)
    {
        var festival = await program.ActiveFestivalAsync(cancellationToken);
        var slug = festival.ResultsNewsId is { } id ? await db.News.Where(n => n.Id == id).Select(n => n.Slug).SingleOrDefaultAsync(cancellationToken) : null;
        var registrations = await db.DanceFestivalRegistrations.Where(r => r.FestivalId == festival.Id).Select(r => r.ReportSentAt).ToListAsync(cancellationToken);
        return new FestivalPublishStatus(festival.ResultsNewsId, slug, festival.ReportsSentAt, registrations.Count(r => r is not null), registrations.Count);
    }

    /// <summary>De uitslag als (openbaar) nieuwsbericht op de website en in de app; opnieuw publiceren werkt het bericht bij.</summary>
    public async Task<Guid> PublishWebsiteAsync(CancellationToken cancellationToken)
    {
        var festival = await AfterAwardsAsync(cancellationToken);
        var results = await judging.ResultsAsync(festival, cancellationToken);
        var body = NewsBody(results);
        var input = new NewsInput(
            $"Uitslag {festival.Name}",
            $"Alle uitslagen van het {festival.Name} op {festival.FestivalDate.ToString("d MMMM yyyy", FestivalPdf.Dutch)}.",
            body, null, null,
            new PublicationInput(ContentVisibility.Public, [], PublicationStatus.Published, null),
            ShowOnWebsite: true);
        if (festival.ResultsNewsId is { } existing && await db.News.AnyAsync(n => n.Id == existing, cancellationToken))
        {
            await content.UpdateNewsAsync(existing, input, cancellationToken);
        }
        else
        {
            festival.ResultsNewsId = await content.CreateNewsAsync(input, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        await audit.WriteAsync(new AuditEntry("festival.results.website", "DanceFestival", festival.Id.ToString(), null, festival.ResultsNewsId.ToString()), cancellationToken);
        return festival.ResultsNewsId!.Value;
    }

    /// <summary>Zet het rapport klaar voor elke vereniging met goedgekeurde dansen die het nog niet heeft gekregen.</summary>
    public async Task<int> SendReportsAsync(CancellationToken cancellationToken)
    {
        var festival = await AfterAwardsAsync(cancellationToken);
        var results = await judging.ResultsAsync(festival, cancellationToken);
        var withDances = results.Categories.SelectMany(c => c.Rows).Select(r => r.RegistrationId).ToHashSet();
        var registrations = await db.DanceFestivalRegistrations
            .Where(r => r.FestivalId == festival.Id && r.ReportSentAt == null).Select(r => r.Id).ToListAsync(cancellationToken);
        var queued = registrations.Where(withDances.Contains).ToList();
        foreach (var id in queued)
        {
            outbox.Enqueue(ReportMailMessageType, new ReportMail(id));
        }

        festival.ReportsSentAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("festival.reports.queued", "DanceFestival", festival.Id.ToString(), null, queued.Count.ToString(CultureInfo.InvariantCulture)), cancellationToken);
        return queued.Count;
    }

    /// <summary>Het rapport van één inschrijving: eigen dansen met punten per jurylid en (anonieme) opmerkingen.</summary>
    public async Task<FestivalReport?> ReportAsync(Guid registrationId, CancellationToken cancellationToken)
    {
        var registration = await db.DanceFestivalRegistrations.AsNoTracking().Include(r => r.Entries).SingleOrDefaultAsync(r => r.Id == registrationId, cancellationToken);
        if (registration is null)
        {
            return null;
        }

        var festival = await db.DanceFestivals.SingleAsync(f => f.Id == registration.FestivalId, cancellationToken);
        var results = await judging.ResultsAsync(festival, cancellationToken);
        var entryIds = registration.Entries.Select(e => e.Id).ToList();
        var numbers = await db.FestivalJurors.AsNoTracking().Where(j => j.FestivalId == festival.Id).ToDictionaryAsync(j => j.UserId, j => j.Number, cancellationToken);
        var scores = await db.FestivalScores.AsNoTracking().Where(s => entryIds.Contains(s.EntryId)).ToListAsync(cancellationToken);
        var comments = await db.FestivalComments.AsNoTracking().Where(c => entryIds.Contains(c.EntryId)).ToListAsync(cancellationToken);
        var dances = new List<ReportDance>();
        foreach (var category in results.Categories)
        {
            foreach (var row in category.Rows.Where(r => r.RegistrationId == registrationId))
            {
                var entry = registration.Entries.Single(e => e.Id == row.EntryId);
                var jurors = scores.Where(s => s.EntryId == entry.Id).GroupBy(s => s.UserId).Where(g => numbers.ContainsKey(g.Key))
                    .Select(g => new ReportJuror(numbers[g.Key], [.. FestivalJudgingService.Criteria.Select(c => g.SingleOrDefault(s => s.Criterion == c)?.Value)]))
                    .OrderBy(j => j.Number).ToList();
                var notes = comments.Where(c => c.EntryId == entry.Id && numbers.ContainsKey(c.UserId))
                    .Select(c => new ReportComment(numbers[c.UserId], c.Text)).OrderBy(c => c.JurorNumber).ToList();
                var names = entry.Title is { Length: > 0 } title ? $"{title} ({entry.Names})" : entry.Names;
                dances.Add(new ReportDance(Heading(entry), ShortHeading(entry), names,
                    $"{entry.Names} · {entry.DancerCount} {(entry.DancerCount == 1 ? "danser" : "dansers")} · {(entry.DancerCount == 1 ? "" : "gem. ")}{entry.AverageAge.ToString("0.#", FestivalPdf.Dutch)} jr · {category.Name}",
                    category.Solo, row.Place, category.Rows.Count, row.Total, jurors, notes));
            }
        }

        return new FestivalReport(registration.Id, registration.ClubName, results, dances);
    }

    /// <summary>"Gardedans · Solo · Senioren", "Showdans · Groep · Jeugd · Karakter".</summary>
    public static string Heading(DanceFestivalEntry e)
    {
        var format = e.Format switch { DanceFormat.Solo => "Solo", DanceFormat.Duo => "Duo", _ => "Groep" };
        var style = e.DanceType == DanceType.Show ? e.ShowStyle == ShowStyle.Modern ? " · Modern" : " · Karakter" : "";
        return $"{ShortHeading(e)} · {format} · {e.Category}{style}";
    }

    public static string ShortHeading(DanceFestivalEntry e) => e.DanceType == DanceType.Garde ? "Gardedans" : "Showdans";

    private async Task<DanceFestival> AfterAwardsAsync(CancellationToken cancellationToken)
    {
        var festival = await program.ActiveFestivalAsync(cancellationToken);
        return festival.AwardsCompletedAt is null
            ? throw new DomainException(ErrorCodes.Validation, "Dit kan pas na de prijsuitreiking: de hoofdjury (of de organisatie) zet die op afgerond.", DomainErrorKind.Conflict)
            : festival;
    }

    /// <summary>Markdown voor het nieuwsbericht: per deel en categorie een tabel met plaats, vereniging, naam en punten.</summary>
    public static string NewsBody(FestivalResults results)
    {
        var md = new StringBuilder();
        md.Append(CultureInfo.InvariantCulture, $"Dit is de uitslag van het {results.FestivalName} op {results.FestivalDate.ToString("dddd d MMMM yyyy", FestivalPdf.Dutch)} in {results.Location}. ")
            .Append("Alle deelnemers: bedankt voor een prachtige dag! Elke vereniging krijgt het juryrapport per e-mail.\n\n");
        foreach (var part in results.Categories.GroupBy(c => c.Part).OrderBy(g => g.Key))
        {
            md.Append("## ").Append(FestivalJudgingService.PartName(part.Key)).Append("\n\n");
            foreach (var category in part.Where(c => c.Rows.Count > 0))
            {
                md.Append("### ").Append(category.Name).Append("\n\n| Plaats | Vereniging | Naam | Punten |\n|---|---|---|---|\n");
                foreach (var row in category.Rows)
                {
                    var name = row.Title is { Length: > 0 } title ? $"{title} ({row.Names})" : row.Names;
                    md.Append(CultureInfo.InvariantCulture, $"| {row.Place} | {Cell(row.Club)} | {Cell(name)} | {row.Total} |\n");
                }

                md.Append('\n');
            }
        }

        return md.ToString();
    }

    private static string Cell(string value) => value.Replace("|", "/", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

/// <summary>Verstuurt het juryrapport van één inschrijving (outbox): PDF als bijlage, van dansgardefestival@.</summary>
public sealed class FestivalReportMailHandler(DrammersDbContext db, FestivalResultsPublisher publisher, IEmailSender email, IClock clock) : IOutboxMessageHandler
{
    public string Type => FestivalResultsPublisher.ReportMailMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var id = JsonSerializer.Deserialize<FestivalResultsPublisher.ReportMail>(message.Payload, JsonSerializerOptions.Web)!.RegistrationId;
        var registration = await db.DanceFestivalRegistrations.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (registration is null || registration.ReportSentAt is not null)
        {
            return;
        }

        var report = await publisher.ReportAsync(id, cancellationToken);
        if (report is null || report.Dances.Count == 0)
        {
            return;
        }

        var pdf = FestivalPdf.Report(report);
        await email.SendAsync(Mail(registration, report, pdf), cancellationToken);
        registration.ReportSentAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }

    public static EmailMessage Mail(DanceFestivalRegistration registration, FestivalReport report, byte[] pdf)
    {
        var festival = report.Results.FestivalName;
        var subject = $"Juryrapport {festival} – {registration.ClubName}";
        var places = string.Join("\n", report.Dances.Select(d => $"- {d.Heading} ({d.Names}): {d.Place}e plaats, {d.Total} punten"));
        var text = $"""
            Beste {registration.ContactName},

            Hartelijk dank voor jullie deelname aan het {festival}! In de bijlage vinden jullie het juryrapport met per dans de punten en de opmerkingen van de jury, en de volledige uitslag.

            {places}

            Met carnavaleske groet,
            De organisatie van het Dansgardefestival
            De Vrolijke Drammers
            """;
        var html = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"<p>Beste {WebUtility.HtmlEncode(registration.ContactName)},</p>")
            .Append(CultureInfo.InvariantCulture, $"<p>Hartelijk dank voor jullie deelname aan het {WebUtility.HtmlEncode(festival)}! In de bijlage vinden jullie het juryrapport met per dans de punten en de opmerkingen van de jury, en de volledige uitslag.</p><ul>");
        foreach (var d in report.Dances)
        {
            html.Append(CultureInfo.InvariantCulture, $"<li>{WebUtility.HtmlEncode(d.Heading)} ({WebUtility.HtmlEncode(d.Names)}): <strong>{d.Place}e plaats</strong>, {d.Total} punten</li>");
        }

        html.Append("</ul><p>Met carnavaleske groet,<br>De organisatie van het Dansgardefestival<br>De Vrolijke Drammers</p>");
        var file = $"juryrapport-{Slugs.From(registration.ClubName)}.pdf";
        return new EmailMessage(registration.Email, subject, text, html.ToString(), FestivalResultsPublisher.SenderAddress, FestivalResultsPublisher.Sender,
            [new EmailFile(file, "application/pdf", pdf)], ViaMailbox: true);
    }
}
