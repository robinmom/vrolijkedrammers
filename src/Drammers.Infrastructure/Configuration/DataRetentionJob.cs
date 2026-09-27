using System.Text.Json;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.AccountRequests;
using Drammers.Modules.Membership.Applications;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Configuration;

public sealed record RetentionResult(
    int DraftApplications, int RejectedApplications, int AccountRequests, int LoginHistory, int PrivacyExports, int OutboxMessages);

/// <summary>
/// Ruimt elke nacht (03:30) gegevens op volgens de bewaartermijnen (<c>config.RetentionPolicy</c>, docs/06 §13):
/// onbevestigde aanmeldingen (7 dagen), afgewezen aanmeldingen, afgehandelde accountverzoeken, aanmeldhistorie,
/// verlopen AVG-exports en verwerkte outbox-berichten (30 dagen). Legt de aantallen vast in de auditlog.
/// </summary>
public sealed class DataRetentionJob(DrammersDbContext db, IFileStore files, IAuditLogger audit, IClock clock) : IRecurringJob
{
    public const string JobName = "data-retention";

    public static readonly TimeSpan DraftLifetime = TimeSpan.FromDays(7);

    public static readonly TimeSpan OutboxLifetime = TimeSpan.FromDays(30);

    public async Task ExecuteAsync(CancellationToken cancellationToken) => await RunAsync(cancellationToken);

    public async Task<RetentionResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var days = await db.RetentionPolicies.AsNoTracking().ToDictionaryAsync(p => p.DataType, p => p.RetentionDays, cancellationToken);
        DateTime Before(string dataType, int fallbackDays) => now.AddDays(-days.GetValueOrDefault(dataType, fallbackDays));

        var draftBefore = now - DraftLifetime;
        var drafts = await db.MembershipApplications
            .Where(a => a.Status == ApplicationStatus.Draft && a.CreatedAt < draftBefore).ExecuteDeleteAsync(cancellationToken);

        var rejectedBefore = Before("membership_application_rejected", 183);
        var rejected = await db.MembershipApplications
            .Where(a => (a.Status == ApplicationStatus.Rejected || a.Status == ApplicationStatus.Withdrawn) && a.DecisionAt < rejectedBefore)
            .ExecuteDeleteAsync(cancellationToken);

        // Afgehandelde accountverzoeken (docs/04: 3 maanden na afhandeling); verzoeken die nog wachten blijven staan.
        var requestsBefore = Before("account_request_rejected", 92);
        var requests = await db.AccountRequests
            .Where(r => r.Status != AccountRequestStatus.Pending && (r.DecidedAt ?? r.RequestedAt) < requestsBefore)
            .ExecuteDeleteAsync(cancellationToken);

        var loginsBefore = Before("login_history", 365);
        var logins = await db.LoginHistory.Where(l => l.OccurredAt < loginsBefore).ExecuteDeleteAsync(cancellationToken);

        var exports = await db.PrivacyRequests.Where(r => r.FilePath != null && r.ExpiresAt < now).ToListAsync(cancellationToken);
        foreach (var export in exports)
        {
            await files.DeleteAsync(FileContainers.Exports, export.FilePath!, cancellationToken);
            export.FilePath = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        var outboxBefore = now - OutboxLifetime;
        var outbox = await db.Outbox.Where(m => m.ProcessedAt != null && m.ProcessedAt < outboxBefore).ExecuteDeleteAsync(cancellationToken);

        var result = new RetentionResult(drafts, rejected, requests, logins, exports.Count, outbox);
        await audit.WriteAsync(new AuditEntry("retention.completed", "RetentionPolicy", "*", null, JsonSerializer.Serialize(result)), cancellationToken);
        return result;
    }
}
