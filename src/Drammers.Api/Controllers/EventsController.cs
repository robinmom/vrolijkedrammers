using Drammers.Api.Content;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Agenda/programma (docs/05 §2): publiek, optioneel ingelogd. Geen toegang = 404 (bestaan niet lekken).</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1")]
public sealed class EventsController(DrammersDbContext db, ContentViewerResolver viewers, ContentUrls urls, IClock clock) : ControllerBase
{
    [HttpGet("event-categories")]
    [PublicCache(300)]
    [ProducesResponseType<IReadOnlyList<EventCategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<EventCategoryResponse>> GetCategories(CancellationToken cancellationToken) =>
        await db.EventCategories.AsNoTracking().OrderBy(c => c.SortOrder)
            .Select(c => new EventCategoryResponse(c.Id, c.Code, c.Name)).ToListAsync(cancellationToken);

    [HttpGet("events")]
    [PublicCache]
    [ProducesResponseType<PagedResult<EventSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<EventSummaryResponse>> Search(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? categoryId, [FromQuery] bool? highlight,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<EventSummaryResponse>.Normalize(page, pageSize);
        var now = clock.UtcNow.UtcDateTime;
        var query = db.Events.AsNoTracking().VisibleTo(await viewers.ResolveAsync(), now);
        var start = from?.ToUniversalTime() ?? now.Date;
        query = query.Where(e => (e.EndAt ?? e.StartAt) >= start);
        if (to is { } t)
        {
            query = query.Where(e => e.StartAt < t.ToUniversalTime());
        }

        if (categoryId is { } c)
        {
            query = query.Where(e => e.CategoryId == c);
        }

        if (highlight == true)
        {
            query = query.Where(e => e.IsHighlight);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(e => e.StartAt).Skip((p - 1) * size).Take(size)
            .Join(db.EventCategories, e => e.CategoryId, cat => cat.Id, (e, cat) => new { e, cat })
            .ToListAsync(cancellationToken);
        var items = new List<EventSummaryResponse>();
        foreach (var r in rows)
        {
            items.Add(new EventSummaryResponse(
                r.e.Id, r.e.Title, r.e.Summary, r.e.StartAt, r.e.EndAt, r.e.AllDay, r.e.LocationName,
                new EventCategoryResponse(r.cat.Id, r.cat.Code, r.cat.Name), r.e.IsHighlight, r.e.BadgeText,
                await urls.ForAsync(FileContainers.Content, r.e.ImageBlobPath, cancellationToken)));
        }

        return new PagedResult<EventSummaryResponse>(items, p, size, total);
    }

    [HttpGet("events/{id:guid}")]
    [PublicCache]
    [ProducesResponseType<EventDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<EventDetailResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().VisibleTo(await viewers.ResolveAsync(), clock.UtcNow.UtcDateTime)
            .Include(x => x.Attachments).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ContentNotFound, "Niet gevonden.", DomainErrorKind.NotFound);
        var category = await db.EventCategories.AsNoTracking().SingleAsync(c => c.Id == e.CategoryId, cancellationToken);
        var attachments = new List<AttachmentResponse>();
        foreach (var a in e.Attachments.OrderBy(a => a.FileName))
        {
            attachments.Add(new AttachmentResponse(a.Id, a.FileName, a.ContentType, a.SizeBytes, (await urls.ForAsync(FileContainers.Content, a.BlobPath, cancellationToken))!));
        }

        return new EventDetailResponse(
            e.Id, e.Title, e.Summary, MarkdownRenderer.ToSafeHtml(e.Description), e.StartAt, e.EndAt, e.AllDay, e.LocationName,
            e.LocationAddress, e.Latitude, e.Longitude, new EventCategoryResponse(category.Id, category.Code, category.Name),
            e.IsHighlight, e.BadgeText, await urls.ForAsync(FileContainers.Content, e.ImageBlobPath, cancellationToken), attachments);
    }

    [HttpGet("events/{id:guid}/ical")]
    [Produces("text/calendar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ical(Guid id, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var e = await db.Events.AsNoTracking().VisibleTo(await viewers.ResolveAsync(), now).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ContentNotFound, "Niet gevonden.", DomainErrorKind.NotFound);
        var location = string.Join(", ", new[] { e.LocationName, e.LocationAddress }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var ics = IcsWriter.Write(e.Id, e.Title, e.Summary, location, e.StartAt, e.EndAt, e.AllDay, now);
        return File(System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", $"drammers-{e.StartAt:yyyyMMdd}.ics");
    }
}

public sealed record EventCategoryResponse(int Id, string Code, string Name);

public sealed record EventSummaryResponse(
    Guid Id, string Title, string? Summary, DateTime StartAt, DateTime? EndAt, bool AllDay, string? LocationName,
    EventCategoryResponse Category, bool IsHighlight, string? BadgeText, string? ImageUrl);

public sealed record EventDetailResponse(
    Guid Id, string Title, string? Summary, string? DescriptionHtml, DateTime StartAt, DateTime? EndAt, bool AllDay,
    string? LocationName, string? LocationAddress, decimal? Latitude, decimal? Longitude, EventCategoryResponse Category,
    bool IsHighlight, string? BadgeText, string? ImageUrl, IReadOnlyList<AttachmentResponse> Attachments);

public sealed record AttachmentResponse(Guid Id, string FileName, string ContentType, long SizeBytes, string Url);
