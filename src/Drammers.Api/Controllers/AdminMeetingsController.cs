using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Api.Content;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Meetings;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Meetings;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>
/// Vergaderingen van het bestuur en de raad van elf, menukop "Vergaderingen" in het portal: de agenda maken, de agenda
/// en de notulen bewaren bij het agenda-item, en de agenda in de huisstijl versturen (docs/05 §6).
/// </summary>
[ApiController]
[Route("api/v1/admin/meetings")]
[RequirePermission(Permissions.MeetingManage)]
public sealed class AdminMeetingsController(
    DrammersDbContext db, MeetingAdministration meetings, ContentAdministration content, ContentUrls urls, MailingPreviewDocuments previews)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<MeetingSummary>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<MeetingSummary>> GetAll([FromQuery] MeetingAudience? audience, CancellationToken cancellationToken) =>
        meetings.ListAsync(audience, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<MeetingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<MeetingResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var meeting = await meetings.GetAsync(id, cancellationToken);
        var audience = await meetings.RecipientsAsync(id, null, cancellationToken);
        return new MeetingResponse(meeting, await DocumentsAsync(meeting.EventId, cancellationToken),
            new MeetingAudienceResponse([.. audience.Recipients], [.. audience.WithoutEmail]));
    }

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Create(MeetingCreateRequest request, CancellationToken cancellationToken)
    {
        var id = await meetings.CreateAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/meetings/{id}", new CreatedResponse(id));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid id, MeetingRequest request, CancellationToken cancellationToken)
    {
        await meetings.UpdateAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    /// <summary>Haalt de vergadering weg; het agenda-item met de bijlagen blijft in de agenda staan.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await meetings.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/agenda")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SaveAgenda(Guid id, MeetingAgendaRequest request, CancellationToken cancellationToken)
    {
        await meetings.SaveAgendaAsync(id, [.. request.Items.Select(i => new MeetingAgendaItemInput(i.Title, i.Note))], cancellationToken);
        return NoContent();
    }

    /// <summary>Neemt de agendapunten over van de vorige vergadering van dezelfde soort.</summary>
    [HttpPost("{id:guid}/agenda/copy-previous")]
    [ProducesResponseType<MeetingAgendaCopiedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<MeetingAgendaCopiedResponse> CopyAgenda(Guid id, CancellationToken cancellationToken) =>
        new(await meetings.CopyAgendaFromPreviousAsync(id, cancellationToken));

    /// <summary>De agenda of de notulen bewaren: een bijlage van het agenda-item, dus ook te lezen in de app.</summary>
    [HttpPost("{id:guid}/documents")]
    [RequestSizeLimit(ContentFiles.MaxAttachmentBytes + 1_000_000)]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> AddDocument(Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        var meeting = await meetings.GetAsync(id, cancellationToken);
        var attachmentId = await content.AddEventAttachmentAsync(meeting.EventId, new UploadedFile(file.FileName, file.Length, file.OpenReadStream), cancellationToken);
        return Created($"/api/v1/admin/meetings/{id}", new CreatedResponse(attachmentId));
    }

    [HttpDelete("{id:guid}/documents/{documentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var meeting = await meetings.GetAsync(id, cancellationToken);
        await content.DeleteEventAttachmentAsync(meeting.EventId, documentId, cancellationToken);
        return NoContent();
    }

    /// <summary>Voorbeeld van de agendamail zoals de ontvanger hem krijgt (niet verstuurd, niet opgeslagen).</summary>
    [HttpPost("{id:guid}/agenda/preview")]
    [ProducesResponseType<MeetingPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<MeetingPreviewResponse> Preview(Guid id, MeetingSendRequest request, CancellationToken cancellationToken)
    {
        var rendered = await meetings.RenderAsync(id, request.DocumentIds ?? [], cancellationToken);
        var audience = await meetings.RecipientsAsync(id, request.ExtraEmails, cancellationToken);
        return new MeetingPreviewResponse(rendered.Subject, rendered.PlainText, previews.Store(rendered.Html), audience.Recipients.Count);
    }

    [HttpPost("{id:guid}/agenda/send")]
    [ProducesResponseType<MeetingSendResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<MeetingSendResponse> Send(Guid id, MeetingSendRequest request, CancellationToken cancellationToken)
    {
        var result = await meetings.SendAgendaAsync(id, request.DocumentIds ?? [], request.ExtraEmails, cancellationToken);
        return new MeetingSendResponse(result.Recipients, result.LastAt);
    }

    private async Task<IReadOnlyList<MeetingDocumentResponse>> DocumentsAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var rows = await db.EventAttachments.AsNoTracking().Where(a => a.EventId == eventId).OrderBy(a => a.FileName)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.SizeBytes, a.BlobPath }).ToListAsync(cancellationToken);
        var documents = new List<MeetingDocumentResponse>(rows.Count);
        foreach (var row in rows)
        {
            documents.Add(new MeetingDocumentResponse(row.Id, row.FileName, row.ContentType, row.SizeBytes,
                (await urls.ForAsync(FileContainers.Content, row.BlobPath, cancellationToken))!));
        }

        return documents;
    }
}

public sealed record MeetingCreateRequest(
    MeetingAudience Audience,
    Guid? EventId,
    [StringLength(200)] string? Title,
    DateTime? StartAt,
    DateTime? EndAt,
    [StringLength(200)] string? LocationName)
{
    public MeetingCreateInput ToInput() => new(Audience, EventId, Title, StartAt, EndAt, LocationName);
}

public sealed record MeetingRequest(
    MeetingAudience Audience,
    [Required, StringLength(200)] string Title,
    DateTime StartAt,
    DateTime? EndAt,
    [StringLength(200)] string? LocationName,
    [StringLength(2000)] string? Intro,
    MeetingSender Sender)
{
    public MeetingDetailsInput ToInput() => new(Audience, Title, StartAt, EndAt, LocationName, Intro, Sender);
}

public sealed record MeetingAgendaItemRequest([Required, StringLength(200)] string Title, [StringLength(1000)] string? Note);

public sealed record MeetingAgendaRequest([Required, MaxLength(60)] IReadOnlyList<MeetingAgendaItemRequest> Items);

/// <summary>Welke documenten meegaan en welke losse adressen er nog bij; leeg = alleen de rolhouders.</summary>
public sealed record MeetingSendRequest([MaxLength(20)] IReadOnlyList<Guid>? DocumentIds, [MaxLength(20)] IReadOnlyList<string>? ExtraEmails);

public sealed record MeetingDocumentResponse(Guid Id, string FileName, string ContentType, long SizeBytes, string Url);

public sealed record MeetingAudienceResponse(IReadOnlyList<MeetingRecipient> Recipients, IReadOnlyList<string> WithoutEmail);

public sealed record MeetingResponse(MeetingView Meeting, IReadOnlyList<MeetingDocumentResponse> Documents, MeetingAudienceResponse Audience);

public sealed record MeetingAgendaCopiedResponse(int Items);

public sealed record MeetingPreviewResponse(string Subject, string PlainText, string PreviewUrl, int Recipients);

public sealed record MeetingSendResponse(int Recipients, DateTime LastAt);
