using System.Net;
using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Notification.Notifications;
using Drammers.Modules.Parade.Parades;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Drammers.Worker.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.ParadeManagement;

/// <summary>Bevestigingsmail na indienen (outbox, na de commit): opgavenummer met de uitleg dat het niet het startnummer is.</summary>
public sealed class ParadeSubmittedMailHandler(DrammersDbContext db, IEmailSender email) : IOutboxMessageHandler
{
    public string Type => ParadeRegistrations.SubmittedMailMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var id = JsonSerializer.Deserialize<ParadeRegistrations.SubmittedMail>(message.Payload, JsonSerializerOptions.Web)!.RegistrationId;
        var registration = await db.ParadeRegistrations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (registration?.ContactEmail is null || registration.RegistrationNumber is null)
        {
            return;
        }

        var parade = await db.Parades.AsNoTracking().SingleAsync(p => p.Id == registration.ParadeId, cancellationToken);
        await email.SendAsync(SubmittedMail(registration, parade), cancellationToken);
    }

    public static EmailMessage SubmittedMail(ParadeRegistration r, Parade parade)
    {
        var subject = $"Inschrijving {parade.Name}: opgavenummer {r.RegistrationNumber}";
        var date = parade.ParadeDate.ToString("d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("nl-NL"));
        var text = $"""
            Beste {r.ContactName},

            Bedankt voor de inschrijving van {r.GroupName} voor de {parade.Name} op {date}.

            Jullie opgavenummer is {r.RegistrationNumber}. Dit is de volgorde van binnenkomst, niet jullie startnummer. Het
            startnummer en de aanrijtijd hoor je later van de optochtcommissie.

            Je kunt de inschrijving bekijken en (binnen de regels) aanpassen in de app, onder Optocht.

            Groeten,
            De Vrolijke Drammers
            """;
        var html = $"""
            <p>Beste {WebUtility.HtmlEncode(r.ContactName)},</p>
            <p>Bedankt voor de inschrijving van <strong>{WebUtility.HtmlEncode(r.GroupName)}</strong> voor de {WebUtility.HtmlEncode(parade.Name)} op {date}.</p>
            <p style="font-size:20px">Jullie opgavenummer is <strong>{r.RegistrationNumber}</strong>.</p>
            <p>Dit is de volgorde van binnenkomst, <strong>niet</strong> jullie startnummer. Het startnummer en de aanrijtijd hoor je later van de optochtcommissie.</p>
            <p>Je kunt de inschrijving bekijken en (binnen de regels) aanpassen in de app, onder Optocht.</p>
            <p>Groeten,<br>De Vrolijke Drammers</p>
            """;
        return new EmailMessage(r.ContactEmail!, subject, text, html);
    }
}

/// <summary>
/// "De inschrijving sluit bijna" (fase 11): elke dag om 18:00; in de laatste 3 dagen één melding per concept aan de
/// beheerders. Alleen app-concepten (niet-leden hebben geen concept op de server).
/// </summary>
public sealed class ParadeDeadlineReminderJob(DrammersDbContext db, INotificationService notifications, IClock clock) : IRecurringJob
{
    public const string JobName = "parade-deadline-reminder";

    public static readonly TimeSpan Window = TimeSpan.FromDays(3);

    public async Task ExecuteAsync(CancellationToken cancellationToken) => await RunAsync(cancellationToken);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var until = now + Window;
        var drafts = await db.ParadeRegistrations
            .Where(r => r.Status == RegistrationStatus.Draft && r.DeadlineReminderSentAt == null
                && db.Parades.Any(p => p.Id == r.ParadeId && p.RegistrationClosesAt > now && p.RegistrationClosesAt <= until))
            .ToListAsync(cancellationToken);
        foreach (var draft in drafts)
        {
            var managers = await db.ParadeRegistrationManagers.Where(m => m.RegistrationId == draft.Id).Select(m => m.UserId).ToListAsync(cancellationToken);
            await notifications.EnqueueAsync(new SystemNotification(
                "Inschrijving optocht sluit bijna",
                $"Je inschrijving{(draft.GroupName is null ? "" : $" van {draft.GroupName}")} is nog een concept. Dien haar op tijd definitief in.",
                NotificationCategory.Reminder, new NotificationAudience(UserIds: managers), "drammers://optocht", "ParadeDeadline", draft.Id), cancellationToken);
            draft.DeadlineReminderSentAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return drafts.Count;
    }
}
