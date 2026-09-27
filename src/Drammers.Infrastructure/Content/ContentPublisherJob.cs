using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Shared;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Content;

/// <summary>
/// Zet geplande content op Gepubliceerd zodra het moment bereikt is. Het audience-filter toont geplande content al vanaf
/// het moment zelf; deze job maakt de status definitief en legt het vast in de auditlog. Aangestuurd door een
/// outbox-bericht op het geplande moment (<see cref="MessageType"/>), plus elke nacht een inhaalrun; niet meer elke
/// minuut, zodat een serverless database kan pauzeren.
/// </summary>
public sealed class ContentPublisherJob(DrammersDbContext db, IAuditLogger audit, IClock clock) : IRecurringJob
{
    public const string JobName = "content-publisher";

    public const string MessageType = "content.publish-due";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        foreach (var e in await db.Events.Where(x => x.Status == PublicationStatus.Scheduled && x.PublishAt <= now).ToListAsync(cancellationToken))
        {
            e.Status = PublicationStatus.Published;
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(new AuditEntry("event.published", "Event", e.Id.ToString(), null, "{\"source\":\"schedule\"}"), cancellationToken);
        }

        foreach (var n in await db.News.Where(x => x.Status == PublicationStatus.Scheduled && x.PublishAt <= now).ToListAsync(cancellationToken))
        {
            n.Status = PublicationStatus.Published;
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(new AuditEntry("news.published", "News", n.Id.ToString(), null, "{\"source\":\"schedule\"}"), cancellationToken);
        }

        foreach (var a in await db.PhotoAlbums.Where(x => x.Status == PublicationStatus.Scheduled && x.PublishAt <= now).ToListAsync(cancellationToken))
        {
            a.Status = PublicationStatus.Published;
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(new AuditEntry("photo-album.published", "PhotoAlbum", a.Id.ToString(), null, "{\"source\":\"schedule\"}"), cancellationToken);
        }
    }
}

/// <summary>Voert <see cref="ContentPublisherJob"/> uit op het geplande moment (worker).</summary>
public sealed class ContentPublishDueHandler(ContentPublisherJob job) : Drammers.Worker.Outbox.IOutboxMessageHandler
{
    public string Type => ContentPublisherJob.MessageType;

    public Task HandleAsync(Drammers.Worker.Outbox.OutboxEnvelope message, CancellationToken cancellationToken) => job.ExecuteAsync(cancellationToken);
}
