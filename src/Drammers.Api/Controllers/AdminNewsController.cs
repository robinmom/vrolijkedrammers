using Drammers.Api.Authorization;
using Drammers.Api.Content;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Shared;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Nieuws beheren, publiceren, plannen en archiveren (docs/05 §6). Push volgt in fase 10.</summary>
[ApiController]
[Route("api/v1/admin/news")]
[RequirePermission(Permissions.NewsManage)]
public sealed class AdminNewsController(DrammersDbContext db, ContentAdministration content, ContentUrls urls) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminNewsSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AdminNewsSummaryResponse>> GetAll(CancellationToken cancellationToken) =>
        await db.News.AsNoTracking().OrderByDescending(n => n.PublishAt ?? n.CreatedAt).Take(200)
            .Select(n => new AdminNewsSummaryResponse(n.Id, n.Title, n.Visibility, n.Status, n.PublishAt, n.ExpireAt))
            .ToListAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminNewsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AdminNewsResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var n = await db.News.AsNoTracking().Include(x => x.Audiences).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ContentNotFound, "Niet gevonden.", DomainErrorKind.NotFound);
        return new AdminNewsResponse(n.Id, n.Title, n.Summary, n.Body, n.Category, n.ExpireAt,
            PublicationResponse.From(n.Visibility, n.Audiences.Select(a => (a.AudienceType, a.AudienceRef)), n.Status, n.PublishAt),
            await urls.ForAsync(FileContainers.Content, n.ImageBlobPath, cancellationToken));
    }

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Create(NewsRequest request, CancellationToken cancellationToken)
    {
        var id = await content.CreateNewsAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/news/{id}", new CreatedResponse(id));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, NewsRequest request, CancellationToken cancellationToken)
    {
        await content.UpdateNewsAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
    {
        await content.SetNewsStatusAsync(id, PublicationStatus.Published, null, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/schedule")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Schedule(Guid id, ScheduleRequest request, CancellationToken cancellationToken)
    {
        await content.SetNewsStatusAsync(id, PublicationStatus.Scheduled, request.PublishAt.ToUniversalTime(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken)
    {
        await content.SetNewsStatusAsync(id, PublicationStatus.Archived, null, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await content.DeleteNewsAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/image")]
    [RequestSizeLimit(ContentFiles.MaxImageBytes + 1_000_000)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetImage(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        await content.SetNewsImageAsync(id, new UploadedFile(file.FileName, file.Length, file.OpenReadStream), cancellationToken);
        return NoContent();
    }
}

public sealed record ScheduleRequest(DateTime PublishAt);

public sealed record AdminNewsSummaryResponse(Guid Id, string Title, ContentVisibility Visibility, PublicationStatus Status, DateTime? PublishAt, DateTime? ExpireAt);

public sealed record AdminNewsResponse(
    Guid Id, string Title, string? Summary, string Body, string? Category, DateTime? ExpireAt, PublicationResponse Publication, string? ImageUrl);
