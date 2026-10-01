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

/// <summary>Nieuws (docs/05 §2): publiek, optioneel ingelogd; alleen gepubliceerd en binnen het publicatievenster.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/news")]
public sealed class NewsController(DrammersDbContext db, ContentViewerResolver viewers, ContentUrls urls, IClock clock, CarnivalSeasons seasons) : ControllerBase
{
    [HttpGet]
    [PublicCache]
    [ProducesResponseType<PagedResult<NewsSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PagedResult<NewsSummaryResponse>> Search(
        [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? season, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<NewsSummaryResponse>.Normalize(page, pageSize);
        var shown = await SeasonEndpoints.ResolveAsync(seasons, season, cancellationToken);
        var visible = db.News.AsNoTracking().VisibleTo(await viewers.ResolveAsync(), clock.UtcNow.UtcDateTime);
        // Zonder jaar: het actieve jaar, altijd met minstens de 5 nieuwste berichten (ook uit het vorige jaar).
        var query = season is null ? visible.CurrentWithLatest(shown) : visible.InSeason(shown);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(n => n.PublishAt).Skip((p - 1) * size).Take(size).ToListAsync(cancellationToken);
        var items = new List<NewsSummaryResponse>();
        foreach (var n in rows)
        {
            items.Add(new NewsSummaryResponse(n.Id, n.Title, n.Summary, n.Category, n.PublishAt!.Value,
                await urls.ForAsync(FileContainers.Content, n.ImageBlobPath, cancellationToken)));
        }

        return new PagedResult<NewsSummaryResponse>(items, p, size, total);
    }

    /// <summary>De oudere carnavalsjaren met nieuws, nieuwste eerst (fase 21g); zonder <c>season</c> toont de lijst het actieve jaar.</summary>
    [HttpGet("seasons")]
    [PublicCache]
    [ProducesResponseType<SeasonsResponse>(StatusCodes.Status200OK)]
    public async Task<SeasonsResponse> Seasons(CancellationToken cancellationToken)
    {
        var calendar = await seasons.LoadAsync(cancellationToken);
        var dates = await db.News.AsNoTracking().VisibleTo(await viewers.ResolveAsync(), clock.UtcNow.UtcDateTime)
            .Where(n => (n.PublishAt ?? n.CreatedAt) < calendar.Current.StartUtc).Select(n => n.PublishAt ?? n.CreatedAt).ToListAsync(cancellationToken);
        return SeasonEndpoints.ToResponse(calendar, calendar.Archive(dates));
    }

    [HttpGet("{id:guid}")]
    [PublicCache]
    [ProducesResponseType<NewsDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<NewsDetailResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var n = await db.News.AsNoTracking().VisibleTo(await viewers.ResolveAsync(), clock.UtcNow.UtcDateTime).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ContentNotFound, "Niet gevonden.", DomainErrorKind.NotFound);
        return new NewsDetailResponse(n.Id, n.Title, n.Summary, MarkdownRenderer.ToSafeHtml(n.Body)!, n.Category, n.PublishAt!.Value,
            await urls.ForAsync(FileContainers.Content, n.ImageBlobPath, cancellationToken));
    }
}

public sealed record NewsSummaryResponse(Guid Id, string Title, string? Summary, string? Category, DateTime PublishedAt, string? ImageUrl);

public sealed record NewsDetailResponse(Guid Id, string Title, string? Summary, string BodyHtml, string? Category, DateTime PublishedAt, string? ImageUrl);
