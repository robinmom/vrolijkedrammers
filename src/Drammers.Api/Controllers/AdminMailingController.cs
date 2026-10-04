using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Api.Content;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Mailings;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Notification.Mailing;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>
/// Mailing (fase 27a), menukop "Mailing" in het portal: mailinggroepen, nieuwsbrieven en uitnodigingen in de huisstijl,
/// met voorbeeld, testmail en versturen; plus de lijst van afmeldingen.
/// </summary>
[ApiController]
[Route("api/v1/admin/mailing")]
[RequirePermission(Permissions.MailingManage)]
public sealed class AdminMailingController(
    DrammersDbContext db, MailingService mailings, WebsiteAdministration website, ContentUrls urls, MailingPreviewDocuments previews) : ControllerBase
{
    // ----- Mailinggroepen ------------------------------------------------------------------------------------------

    [HttpGet("lists")]
    [ProducesResponseType<IReadOnlyList<MailingListSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<MailingListSummaryResponse>> GetLists(CancellationToken cancellationToken) =>
        await db.MailingLists.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new MailingListSummaryResponse(l.Id, l.Name, l.Description, l.AllMembers, l.Members.Count, l.Groups.Count, l.Addresses.Count))
            .ToListAsync(cancellationToken);

    [HttpGet("lists/{id:guid}")]
    [ProducesResponseType<MailingListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<MailingListResponse> GetList(Guid id, CancellationToken cancellationToken)
    {
        var list = await db.MailingLists.AsNoTracking().Include(l => l.Members).Include(l => l.Groups).Include(l => l.Addresses)
            .SingleOrDefaultAsync(l => l.Id == id, cancellationToken) ?? throw new DomainException(ErrorCodes.NotFound, "Mailinggroep niet gevonden.", DomainErrorKind.NotFound);
        var memberIds = list.Members.Select(m => m.MemberId).ToList();
        var members = await db.Members.AsNoTracking().Where(m => memberIds.Contains(m.Id)).OrderBy(m => m.FullName)
            .Select(m => new MailingListMemberResponse(m.Id, m.FullName, m.MemberNumber, m.Email)).ToListAsync(cancellationToken);
        var groupIds = list.Groups.Select(g => g.GroupId).ToList();
        var groups = await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).OrderBy(g => g.Name)
            .Select(g => new MailingListGroupResponse(g.Id, g.Name)).ToListAsync(cancellationToken);
        var audience = await mailings.ResolveAsync([id], cancellationToken);
        return new MailingListResponse(list.Id, list.Name, list.Description, list.AllMembers, members, groups,
            [.. list.Addresses.OrderBy(a => a.Email).Select(a => new MailingAddressResponse(a.Email, a.Name))],
            new MailingAudienceResponse(audience.Recipients.Count, audience.Unsubscribed, audience.WithoutEmail));
    }

    [HttpPost("lists")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> CreateList(MailingListRequest request, CancellationToken cancellationToken)
    {
        var id = await mailings.CreateListAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/mailing/lists/{id}", new CreatedResponse(id));
    }

    [HttpPut("lists/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateList(Guid id, MailingListRequest request, CancellationToken cancellationToken)
    {
        await mailings.UpdateListAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("lists/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeleteList(Guid id, CancellationToken cancellationToken)
    {
        await mailings.DeleteListAsync(id, cancellationToken);
        return NoContent();
    }

    // ----- Mailings ------------------------------------------------------------------------------------------------

    [HttpGet("mailings")]
    [ProducesResponseType<IReadOnlyList<MailingSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<MailingSummaryResponse>> GetMailings(CancellationToken cancellationToken) =>
        await db.Mailings.AsNoTracking().OrderByDescending(m => m.SentAt ?? m.UpdatedAt ?? m.CreatedAt)
            .Select(m => new MailingSummaryResponse(m.Id, m.Kind, m.Subject, m.Status, m.RecipientCount,
                db.MailingRecipients.Count(r => r.MailingId == m.Id && r.Status == MailingRecipientStatus.Sent), m.SentAt, m.UpdatedAt ?? m.CreatedAt))
            .ToListAsync(cancellationToken);

    [HttpGet("mailings/{id:guid}")]
    [ProducesResponseType<MailingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<MailingResponse> GetMailing(Guid id, CancellationToken cancellationToken)
    {
        var m = await db.Mailings.AsNoTracking().Include(x => x.Lists).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Mailing niet gevonden.", DomainErrorKind.NotFound);
        var counts = await db.MailingRecipients.AsNoTracking().Where(r => r.MailingId == id).GroupBy(r => r.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var images = await ImagesAsync(MailingService.ReadBlocks(m.Blocks), cancellationToken);
        return new MailingResponse(m.Id, m.Kind, m.Subject, m.Preheader,
            [.. MailingService.ReadBlocks(m.Blocks).Select(b => MailingBlockDto.From(b, b.Image is null ? null : images.GetValueOrDefault(b.Image)))],
            [.. m.Lists.Select(l => l.ListId)], m.Status, m.SentAt, m.RecipientCount,
            new MailingProgressResponse(counts.GetValueOrDefault(MailingRecipientStatus.Pending), counts.GetValueOrDefault(MailingRecipientStatus.Sent),
                counts.GetValueOrDefault(MailingRecipientStatus.Failed)));
    }

    [HttpPost("mailings")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> CreateMailing(MailingRequest request, CancellationToken cancellationToken)
    {
        var id = await mailings.CreateAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/mailing/mailings/{id}", new CreatedResponse(id));
    }

    [HttpPut("mailings/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateMailing(Guid id, MailingRequest request, CancellationToken cancellationToken)
    {
        await mailings.UpdateAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("mailings/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeleteMailing(Guid id, CancellationToken cancellationToken)
    {
        await mailings.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("mailings/{id:guid}/duplicate")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatedResponse>> DuplicateMailing(Guid id, CancellationToken cancellationToken)
    {
        var copy = await mailings.DuplicateAsync(id, cancellationToken);
        return Created($"/api/v1/admin/mailing/mailings/{copy}", new CreatedResponse(copy));
    }

    /// <summary>Voorbeeld van de mail zoals een lid hem krijgt (niet opgeslagen), met het aantal ontvangers van de gekozen groepen.</summary>
    [HttpPost("preview")]
    [ProducesResponseType<MailingPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<MailingPreviewResponse> Preview(MailingRequest request, CancellationToken cancellationToken)
    {
        var input = request.ToInput();
        MailingRenderer.Validate(input.Blocks);
        var images = await ImagesAsync(input.Blocks, cancellationToken);
        var draft = new Mailing
        {
            Subject = input.Subject,
            Preheader = input.Preheader,
            Blocks = System.Text.Json.JsonSerializer.Serialize(input.Blocks, System.Text.Json.JsonSerializerOptions.Web),
        };
        var rendered = mailings.Render(draft, new MailingPerson("Piet", "Piet van der Drammer"), null, path => images.GetValueOrDefault(path));
        var audience = await mailings.ResolveAsync([.. input.ListIds.Distinct()], cancellationToken);
        return new MailingPreviewResponse(rendered.Subject, rendered.Html, rendered.PlainText,
            new MailingAudienceResponse(audience.Recipients.Count, audience.Unsubscribed, audience.WithoutEmail), previews.Store(rendered.Html));
    }

    [HttpPost("mailings/{id:guid}/test")]
    [ProducesResponseType<MailingTestResponse>(StatusCodes.Status200OK)]
    public async Task<MailingTestResponse> SendTest(Guid id, CancellationToken cancellationToken) =>
        new(await mailings.SendTestAsync(id, cancellationToken));

    [HttpPost("mailings/{id:guid}/send")]
    [ProducesResponseType<MailingSendResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<MailingSendResponse> Send(Guid id, CancellationToken cancellationToken)
    {
        var (recipients, lastAt) = await mailings.SendAsync(id, cancellationToken);
        return new MailingSendResponse(recipients, lastAt);
    }

    [HttpPost("images")]
    [RequestSizeLimit(ContentFiles.MaxImageBytes + 1_000_000)]
    [ProducesResponseType<UploadedImageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<UploadedImageResponse> UploadImage(IFormFile file, CancellationToken cancellationToken)
    {
        var stored = await website.StoreImageAsync(new UploadedFile(file.FileName, file.Length, file.OpenReadStream), cancellationToken);
        return new UploadedImageResponse(stored.Path, (await urls.ForAsync(FileContainers.Content, stored.Path, cancellationToken))!);
    }

    // ----- Afmeldingen ---------------------------------------------------------------------------------------------

    [HttpGet("unsubscribes")]
    [ProducesResponseType<IReadOnlyList<MailingUnsubscribeResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<MailingUnsubscribeResponse>> GetUnsubscribes(CancellationToken cancellationToken) =>
        await db.MailingUnsubscribes.AsNoTracking().OrderByDescending(u => u.UnsubscribedAt)
            .Select(u => new MailingUnsubscribeResponse(u.Email, u.UnsubscribedAt)).ToListAsync(cancellationToken);

    /// <summary>Weer aanmelden, alleen op verzoek van de persoon zelf.</summary>
    [HttpDelete("unsubscribes")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Resubscribe([FromQuery, Required] string email, CancellationToken cancellationToken)
    {
        await mailings.ResubscribeAsync(email, cancellationToken);
        return NoContent();
    }

    private async Task<Dictionary<string, string>> ImagesAsync(IEnumerable<MailingBlock> blocks, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>();
        foreach (var path in blocks.Select(b => b.Image).OfType<string>().Where(UploadedImages.IsUploadPath).Distinct())
        {
            if (await urls.ForAsync(FileContainers.Content, path, cancellationToken) is { } url)
            {
                result[path] = url;
            }
        }

        return result;
    }
}

public sealed record MailingListRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [StringLength(500)] string? Description,
    bool AllMembers,
    [MaxLength(5000)] IReadOnlyList<Guid>? MemberIds,
    [MaxLength(200)] IReadOnlyList<Guid>? GroupIds,
    [MaxLength(5000)] IReadOnlyList<MailingAddressRequest>? Addresses)
{
    public MailingListInput ToInput() =>
        new(Name, Description, AllMembers, MemberIds ?? [], GroupIds ?? [], [.. (Addresses ?? []).Select(a => new MailingAddressInput(a.Email, a.Name))]);
}

public sealed record MailingAddressRequest([Required, StringLength(254)] string Email, [StringLength(150)] string? Name);

public sealed record MailingListSummaryResponse(Guid Id, string Name, string? Description, bool AllMembers, int MemberCount, int GroupCount, int AddressCount);

public sealed record MailingListMemberResponse(Guid Id, string FullName, string? MemberNumber, string? Email);

public sealed record MailingListGroupResponse(Guid Id, string Name);

public sealed record MailingAddressResponse(string Email, string? Name);

public sealed record MailingAudienceResponse(int Recipients, int Unsubscribed, int WithoutEmail);

public sealed record MailingListResponse(
    Guid Id, string Name, string? Description, bool AllMembers, IReadOnlyList<MailingListMemberResponse> Members,
    IReadOnlyList<MailingListGroupResponse> Groups, IReadOnlyList<MailingAddressResponse> Addresses, MailingAudienceResponse Audience);

/// <summary>Een blok; <c>imageUrl</c> alleen in antwoorden (om de foto in het portal te tonen).</summary>
public sealed record MailingBlockDto(
    [Required, StringLength(20)] string Type,
    [StringLength(10000)] string? Text,
    [StringLength(100)] string? Label,
    [StringLength(500)] string? Url,
    [StringLength(100)] string? Image,
    [StringLength(300)] string? Note,
    string? ImageUrl = null)
{
    public MailingBlock ToBlock() => new(Type, Text, Label, Url, Image, Note);

    public static MailingBlockDto From(MailingBlock b, string? imageUrl) => new(b.Type, b.Text, b.Label, b.Url, b.Image, b.Note, imageUrl);
}

public sealed record MailingRequest(
    MailingKind Kind,
    [Required, StringLength(200, MinimumLength = 2)] string Subject,
    [StringLength(200)] string? Preheader,
    [Required, MaxLength(MailingRenderer.MaxBlocks)] IReadOnlyList<MailingBlockDto> Blocks,
    [MaxLength(50)] IReadOnlyList<Guid>? ListIds)
{
    public MailingInput ToInput() => new(Kind, Subject, Preheader, [.. Blocks.Select(b => b.ToBlock())], ListIds ?? []);
}

public sealed record MailingSummaryResponse(
    Guid Id, MailingKind Kind, string Subject, MailingStatus Status, int RecipientCount, int SentCount, DateTime? SentAt, DateTime UpdatedAt);

public sealed record MailingProgressResponse(int Pending, int Sent, int Failed);

public sealed record MailingResponse(
    Guid Id, MailingKind Kind, string Subject, string? Preheader, IReadOnlyList<MailingBlockDto> Blocks, IReadOnlyList<Guid> ListIds, MailingStatus Status,
    DateTime? SentAt, int RecipientCount, MailingProgressResponse Progress);

/// <summary><c>PreviewUrl</c>: het voorbeeld als eigen pagina (tien minuten geldig), om in een iframe te tonen.</summary>
public sealed record MailingPreviewResponse(string Subject, string Html, string PlainText, MailingAudienceResponse Audience, string PreviewUrl);

public sealed record MailingTestResponse(string SentTo);

public sealed record MailingSendResponse(int Recipients, DateTime LastAt);

public sealed record MailingUnsubscribeResponse(string Email, DateTime UnsubscribedAt);
