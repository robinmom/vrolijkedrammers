using Drammers.Api.Authorization;
using Drammers.Api.Content;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Agenda en programma beheren (docs/05 §6).</summary>
[ApiController]
[Route("api/v1/admin/events")]
[RequirePermission(Permissions.EventManage)]
public sealed class AdminEventsController(DrammersDbContext db, ContentAdministration content, ContentUrls urls) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminEventSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AdminEventSummaryResponse>> GetAll([FromQuery] bool includePast, CancellationToken cancellationToken)
    {
        var query = db.Events.AsNoTracking();
        if (!includePast)
        {
            var today = DateTime.UtcNow.Date;
            query = query.Where(e => (e.EndAt ?? e.StartAt) >= today);
        }

        return await query.OrderBy(e => e.StartAt)
            .Select(e => new AdminEventSummaryResponse(e.Id, e.Title, e.StartAt, e.Visibility, e.Status, e.PublishAt, e.IsHighlight))
            .ToListAsync(cancellationToken);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminEventResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AdminEventResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var e = await db.Events.AsNoTracking().Include(x => x.Audiences).Include(x => x.Attachments).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ContentNotFound, "Niet gevonden.", DomainErrorKind.NotFound);
        var attachments = new List<AttachmentResponse>();
        foreach (var a in e.Attachments.OrderBy(a => a.FileName))
        {
            attachments.Add(new AttachmentResponse(a.Id, a.FileName, a.ContentType, a.SizeBytes, (await urls.ForAsync(FileContainers.Content, a.BlobPath, cancellationToken))!));
        }

        return new AdminEventResponse(
            e.Id, e.CategoryId, e.Title, e.Summary, e.Description, e.StartAt, e.EndAt, e.AllDay, e.LocationName, e.LocationAddress,
            e.Latitude, e.Longitude, e.IsHighlight, e.BadgeText,
            new PublicationResponse(e.Visibility, [.. e.Audiences.Select(a => a.AudienceRef)], e.Status, e.PublishAt),
            await urls.ForAsync(FileContainers.Content, e.ImageBlobPath, cancellationToken), attachments);
    }

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Create(EventRequest request, CancellationToken cancellationToken)
    {
        var id = await content.CreateEventAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/events/{id}", new CreatedResponse(id));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, EventRequest request, CancellationToken cancellationToken)
    {
        await content.UpdateEventAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await content.DeleteEventAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/image")]
    [RequestSizeLimit(ContentFiles.MaxImageBytes + 1_000_000)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetImage(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        await content.SetEventImageAsync(id, new UploadedFile(file.FileName, file.Length, file.OpenReadStream), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/attachments")]
    [RequestSizeLimit(ContentFiles.MaxAttachmentBytes + 1_000_000)]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> AddAttachment(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        var attachmentId = await content.AddEventAttachmentAsync(id, new UploadedFile(file.FileName, file.Length, file.OpenReadStream), cancellationToken);
        return Created($"/api/v1/admin/events/{id}", new CreatedResponse(attachmentId));
    }

    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAttachment(Guid id, Guid attachmentId, CancellationToken cancellationToken)
    {
        await content.DeleteEventAttachmentAsync(id, attachmentId, cancellationToken);
        return NoContent();
    }
}

public sealed record AdminEventSummaryResponse(
    Guid Id, string Title, DateTime StartAt, Modules.Content.Shared.ContentVisibility Visibility,
    Modules.Content.Shared.PublicationStatus Status, DateTime? PublishAt, bool IsHighlight);

public sealed record AdminEventResponse(
    Guid Id, int CategoryId, string Title, string? Summary, string? Description, DateTime StartAt, DateTime? EndAt, bool AllDay,
    string? LocationName, string? LocationAddress, decimal? Latitude, decimal? Longitude, bool IsHighlight, string? BadgeText,
    PublicationResponse Publication, string? ImageUrl, IReadOnlyList<AttachmentResponse> Attachments);
