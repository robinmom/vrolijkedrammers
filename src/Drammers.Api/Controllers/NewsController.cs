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
public sealed class NewsController(DrammersDbContext db, ContentViewerResolver viewers, ContentUrls urls, IClock clock) : ControllerBase
{
    [HttpGet]
    [PublicCache]
    [ProducesResponseType<PagedResult<NewsSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<NewsSummaryResponse>> Search([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<NewsSummaryResponse>.Normalize(page, pageSize);
        var query = db.News.AsNoTracking().VisibleTo(await viewers.ResolveAsync(), clock.UtcNow.UtcDateTime);
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
