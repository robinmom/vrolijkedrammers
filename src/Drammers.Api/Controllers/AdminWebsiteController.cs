using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Api.Content;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Content.Import;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Website;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>
/// Websitebeheer (fase 21a), menukop "Website" in het portal: homepage (hero), pagina's, kader, prinsen,
/// onderscheidingen en instellingen. Afbeeldingen eerst uploaden via <c>POST images</c>, daarna het pad meesturen.
/// </summary>
[ApiController]
[Route("api/v1/admin/website")]
[RequirePermission(Permissions.WebsiteManage)]
public sealed class AdminWebsiteController(DrammersDbContext db, WebsiteAdministration website, ContentUrls urls) : ControllerBase
{
    [HttpPost("images")]
    [RequestSizeLimit(ContentFiles.MaxImageBytes + 1_000_000)]
    [ProducesResponseType<UploadedImageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<UploadedImageResponse> UploadImage(IFormFile file, CancellationToken cancellationToken)
    {
        var stored = await website.StoreImageAsync(new UploadedFile(file.FileName, file.Length, file.OpenReadStream), cancellationToken);
        return new UploadedImageResponse(stored.Path, (await ImageUrl(stored.Path, cancellationToken))!);
    }

    // ----- Homepage en instellingen --------------------------------------------------------------------------------

    [HttpGet("settings")]
    [ProducesResponseType<WebsiteSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<WebsiteSettingsResponse> GetSettings(CancellationToken cancellationToken)
    {
        var s = await website.GetSettingsAsync(cancellationToken);
        return new WebsiteSettingsResponse(s.HeroEyebrow, s.HeroTitle, s.HeroSubtitle, s.HeroPrimaryLabel, s.HeroPrimaryLink,
            s.HeroSecondaryLabel, s.HeroSecondaryLink, await ImageUrl(s.HeroImageBlobPath, cancellationToken), s.FacebookPageUrl, s.InstagramUrl,
            s.ShowYouthPrinces, await db.Princes.CountAsync(p => p.Kind == PrinceKind.YouthPrince, cancellationToken));
    }

    [HttpPut("settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateSettings(WebsiteSettingsRequest request, CancellationToken cancellationToken)
    {
        await website.UpdateSettingsAsync(new WebsiteSettingsInput(request.HeroEyebrow, request.HeroTitle, request.HeroSubtitle,
            request.HeroPrimaryLabel, request.HeroPrimaryLink, request.HeroSecondaryLabel, request.HeroSecondaryLink, request.HeroImage,
            request.FacebookPageUrl, request.InstagramUrl, request.ShowYouthPrinces), cancellationToken);
        return NoContent();
    }

    // ----- Pagina's ------------------------------------------------------------------------------------------------

    [HttpGet("pages")]
    [ProducesResponseType<IReadOnlyList<WebsitePageSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<WebsitePageSummaryResponse>> GetPages(CancellationToken cancellationToken) =>
        await db.WebsitePages.AsNoTracking().OrderBy(p => p.Menu).ThenBy(p => p.SortOrder).ThenBy(p => p.Title)
            .Select(p => new WebsitePageSummaryResponse(p.Id, p.Slug, p.Title, p.IsPublished, p.SortOrder, p.UpdatedAt ?? p.CreatedAt, p.Menu))
            .ToListAsync(cancellationToken);

    /// <summary>Albums om onder een pagina te zetten (zonder fotobeheer-rechten), nieuwste eerst.</summary>
    [HttpGet("albums")]
    [ProducesResponseType<IReadOnlyList<WebsiteAlbumOptionResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<WebsiteAlbumOptionResponse>> GetAlbums(CancellationToken cancellationToken) =>
        await db.PhotoAlbums.AsNoTracking().OrderByDescending(a => a.AlbumDate).ThenBy(a => a.Title)
            .Select(a => new WebsiteAlbumOptionResponse(a.Id, a.Title, a.AlbumDate, db.Photos.Count(p => p.AlbumId == a.Id)))
            .ToListAsync(cancellationToken);

    [HttpGet("pages/{id:guid}")]
    [ProducesResponseType<WebsitePageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<WebsitePageResponse> GetPage(Guid id, CancellationToken cancellationToken)
    {
        var p = await db.WebsitePages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw PageNotFound();
        return new WebsitePageResponse(p.Id, p.Slug, p.Title, p.Intro, p.Body, await ImageUrl(p.ImageBlobPath, cancellationToken), p.IsPublished, p.SortOrder,
            p.Menu, p.PhotoAlbumId);
    }

    [HttpPost("pages")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> CreatePage(WebsitePageRequest request, CancellationToken cancellationToken)
    {
        var id = await website.CreatePageAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/website/pages/{id}", new CreatedResponse(id));
    }

    [HttpPut("pages/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdatePage(Guid id, WebsitePageRequest request, CancellationToken cancellationToken)
    {
        await website.UpdatePageAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("pages/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePage(Guid id, CancellationToken cancellationToken)
    {
        await website.DeletePageAsync(id, cancellationToken);
        return NoContent();
    }

    // ----- Kader ---------------------------------------------------------------------------------------------------

    [HttpGet("committees")]
    [ProducesResponseType<IReadOnlyList<CommitteeResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<CommitteeResponse>> GetCommittees(CancellationToken cancellationToken)
    {
        var committees = await db.Committees.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);
        var members = await db.CommitteeMembers.AsNoTracking().OrderBy(m => m.SortOrder)
            .GroupJoin(db.Members.AsNoTracking(), k => k.MemberId, m => m.Id, (k, ms) => new { k, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new { x.k, MemberNumber = m == null ? null : m.MemberNumber })
            .ToListAsync(cancellationToken);
        var result = new List<CommitteeResponse>();
        foreach (var c in committees)
        {
            var list = new List<CommitteeMemberResponse>();
            foreach (var x in members.Where(x => x.k.CommitteeId == c.Id))
            {
                list.Add(new CommitteeMemberResponse(x.k.Id, x.k.CommitteeId, x.k.MemberId, x.MemberNumber, x.k.Name, x.k.Function,
                    await ImageUrl(x.k.PhotoBlobPath, cancellationToken), x.k.SortOrder));
            }

            result.Add(new CommitteeResponse(c.Id, c.Name, c.Slug, c.SortOrder, list));
        }

        return result;
    }

    /// <summary>Leden zoeken om als kaderlid te kiezen; alleen lidnummer, naam en woonplaats (geen andere persoonsgegevens).</summary>
    [HttpGet("member-search")]
    [ProducesResponseType<IReadOnlyList<KaderCandidateResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<KaderCandidateResponse>> SearchMembers([FromQuery] string? q, CancellationToken cancellationToken)
    {
        var term = q?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < 2)
        {
            return [];
        }

        return await db.Members.AsNoTracking()
            .Where(m => m.FullName.Contains(term) || m.MemberNumber == term)
            .OrderBy(m => m.FullName).Take(20)
            .Select(m => new KaderCandidateResponse(m.Id, m.MemberNumber, m.FullName, m.City))
            .ToListAsync(cancellationToken);
    }

    [HttpPost("committees")]
    [ProducesResponseType<CreatedIntResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatedIntResponse>> CreateCommittee(CommitteeRequest request, CancellationToken cancellationToken)
    {
        var id = await website.CreateCommitteeAsync(new CommitteeInput(request.Name, request.SortOrder), cancellationToken);
        return Created($"/api/v1/admin/website/committees/{id}", new CreatedIntResponse(id));
    }

    [HttpPut("committees/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateCommittee(int id, CommitteeRequest request, CancellationToken cancellationToken)
    {
        await website.UpdateCommitteeAsync(id, new CommitteeInput(request.Name, request.SortOrder), cancellationToken);
        return NoContent();
    }

    [HttpDelete("committees/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeleteCommittee(int id, CancellationToken cancellationToken)
    {
        await website.DeleteCommitteeAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("committees/{id:int}/order")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ReorderCommittee(int id, KaderOrderRequest request, CancellationToken cancellationToken)
    {
        await website.ReorderCommitteeAsync(id, request.Ids, cancellationToken);
        return NoContent();
    }

    [HttpPost("kader")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> AddKader(CommitteeMemberRequest request, CancellationToken cancellationToken)
    {
        var id = await website.AddCommitteeMemberAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/website/kader/{id}", new CreatedResponse(id));
    }

    [HttpPut("kader/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateKader(Guid id, CommitteeMemberRequest request, CancellationToken cancellationToken)
    {
        await website.UpdateCommitteeMemberAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("kader/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveKader(Guid id, CancellationToken cancellationToken)
    {
        await website.RemoveCommitteeMemberAsync(id, cancellationToken);
        return NoContent();
    }

    // ----- Prinsen -------------------------------------------------------------------------------------------------

    [HttpGet("princes")]
    [ProducesResponseType<IReadOnlyList<PrinceResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<PrinceResponse>> GetPrinces([FromQuery] PrinceKind? kind, CancellationToken cancellationToken)
    {
        var princes = await db.Princes.AsNoTracking().Where(p => kind == null || p.Kind == kind)
            .OrderByDescending(p => p.Year).ThenBy(p => p.PrinceName).ToListAsync(cancellationToken);
        var result = new List<PrinceResponse>();
        foreach (var p in princes)
        {
            result.Add(new PrinceResponse(p.Id, p.Kind, p.Year, p.PrinceName, p.Name, p.Motto, await ImageUrl(p.PhotoBlobPath, cancellationToken)));
        }

        return result;
    }

    [HttpPost("princes")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> CreatePrince(PrinceRequest request, CancellationToken cancellationToken)
    {
        var id = await website.CreatePrinceAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/website/princes/{id}", new CreatedResponse(id));
    }

    [HttpPut("princes/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdatePrince(Guid id, PrinceRequest request, CancellationToken cancellationToken)
    {
        await website.UpdatePrinceAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("princes/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePrince(Guid id, CancellationToken cancellationToken)
    {
        await website.DeletePrinceAsync(id, cancellationToken);
        return NoContent();
    }

    // ----- Onderscheidingen ----------------------------------------------------------------------------------------

    [HttpGet("awards")]
    [ProducesResponseType<IReadOnlyList<AwardResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AwardResponse>> GetAwards([FromQuery] AwardType? type, CancellationToken cancellationToken)
    {
        var awards = await db.Awards.AsNoTracking().Where(a => type == null || a.Type == type)
            .OrderByDescending(a => a.Year).ThenBy(a => a.Type).ThenBy(a => a.Recipient).ToListAsync(cancellationToken);
        var result = new List<AwardResponse>();
        foreach (var a in awards)
        {
            result.Add(new AwardResponse(a.Id, a.Type, a.Year, a.Recipient, a.Body, await ImageUrl(a.PhotoBlobPath, cancellationToken), a.Slug, a.IsPublished));
        }

        return result;
    }

    [HttpPost("awards")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> CreateAward(AwardRequest request, CancellationToken cancellationToken)
    {
        var id = await website.CreateAwardAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/website/awards/{id}", new CreatedResponse(id));
    }

    [HttpPut("awards/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAward(Guid id, AwardRequest request, CancellationToken cancellationToken)
    {
        await website.UpdateAwardAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("awards/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAward(Guid id, CancellationToken cancellationToken)
    {
        await website.DeleteAwardAsync(id, cancellationToken);
        return NoContent();
    }

    // ----- Overzetten van de oude website (fase 21e) ----------------------------------------------------------------

    [HttpGet("import")]
    [ProducesResponseType<WebsiteImportSummary>(StatusCodes.Status200OK)]
    public async Task<WebsiteImportSummary> GetImport(CancellationToken cancellationToken)
    {
        var counts = await db.WebsiteImportItems.AsNoTracking().GroupBy(i => new { i.Kind, i.Status })
            .Select(g => new { g.Key.Kind, g.Key.Status, Count = g.Count() }).ToListAsync(cancellationToken);
        var failures = await db.WebsiteImportItems.AsNoTracking().Where(i => i.Status == WebsiteImportStatus.Failed).OrderBy(i => i.Id).Take(100)
            .Select(i => new WebsiteImportFailure(i.Id, i.Kind, i.Title, i.SourceUrl, i.Error)).ToListAsync(cancellationToken);
        var last = await db.WebsiteImportItems.MaxAsync(i => (DateTime?)(i.ProcessedAt ?? i.CreatedAt), cancellationToken);
        var running = await db.Outbox.AnyAsync(o => o.Type == WebsiteImporter.MessageType && o.ProcessedAt == null && o.Attempts < Drammers.Worker.Outbox.OutboxProcessor.MaxAttempts, cancellationToken);
        int Count(WebsiteImportKind kind, WebsiteImportStatus status) => counts.Where(c => c.Kind == kind && c.Status == status).Sum(c => c.Count);
        return new WebsiteImportSummary(
            [.. Enum.GetValues<WebsiteImportKind>().Select(k => new WebsiteImportCount(k, Count(k, WebsiteImportStatus.Pending), Count(k, WebsiteImportStatus.Done),
                Count(k, WebsiteImportStatus.Skipped), Count(k, WebsiteImportStatus.Failed)))],
            failures, running, last);
    }

    /// <summary>Start (of hervat) het overzetten van de oude WordPress-site; het werk gebeurt op de achtergrond.</summary>
    [HttpPost("import")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> StartImport([FromServices] Drammers.SharedKernel.Messaging.IOutbox outbox, CancellationToken cancellationToken)
    {
        if (!await db.Outbox.AnyAsync(o => o.Type == WebsiteImporter.MessageType && o.ProcessedAt == null && o.Attempts < Drammers.Worker.Outbox.OutboxProcessor.MaxAttempts, cancellationToken))
        {
            // Plannen is veilig om te herhalen: alleen nieuwe items van de oude site komen erbij.
            outbox.Enqueue(WebsiteImporter.MessageType, new WebsiteImporter.ImportMessage("plan"));
            await db.SaveChangesAsync(cancellationToken);
        }

        return Accepted();
    }

    /// <summary>Mislukte items opnieuw proberen.</summary>
    [HttpPost("import/retry")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RetryImport([FromServices] Drammers.SharedKernel.Messaging.IOutbox outbox, CancellationToken cancellationToken)
    {
        await db.WebsiteImportItems.Where(i => i.Status == WebsiteImportStatus.Failed)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.Status, WebsiteImportStatus.Pending), cancellationToken);
        return await StartImport(outbox, cancellationToken);
    }

    private Task<string?> ImageUrl(string? path, CancellationToken cancellationToken) => urls.ForAsync(FileContainers.Content, path, cancellationToken);

    private static DomainException PageNotFound() => new(ErrorCodes.ContentNotFound, "Niet gevonden.", DomainErrorKind.NotFound);
}

public sealed record WebsiteSettingsRequest(
    [StringLength(80)] string? HeroEyebrow,
    [Required, StringLength(120, MinimumLength = 2)] string HeroTitle,
    [StringLength(300)] string? HeroSubtitle,
    [StringLength(40)] string? HeroPrimaryLabel,
    WebsiteLink? HeroPrimaryLink,
    [StringLength(40)] string? HeroSecondaryLabel,
    WebsiteLink? HeroSecondaryLink,
    [StringLength(300)] string? HeroImage,
    [StringLength(300)] string? FacebookPageUrl,
    [StringLength(300)] string? InstagramUrl,
    bool ShowYouthPrinces);

public sealed record WebsiteSettingsResponse(
    string? HeroEyebrow, string HeroTitle, string? HeroSubtitle, string? HeroPrimaryLabel, WebsiteLink? HeroPrimaryLink,
    string? HeroSecondaryLabel, WebsiteLink? HeroSecondaryLink, string? HeroImageUrl, string? FacebookPageUrl, string? InstagramUrl,
    bool ShowYouthPrinces, int YouthPrinceCount);

public sealed record WebsitePageRequest(
    [StringLength(100)] string? Slug,
    [Required, StringLength(200, MinimumLength = 2)] string Title,
    [StringLength(500)] string? Intro,
    [Required(AllowEmptyStrings = true), StringLength(100000)] string Body,
    [StringLength(300)] string? Image,
    bool IsPublished,
    int SortOrder = 0,
    WebsiteMenu Menu = WebsiteMenu.None,
    Guid? PhotoAlbumId = null)
{
    public WebsitePageInput ToInput() => new(Slug, Title, Intro, Body, Image, IsPublished, SortOrder, Menu, PhotoAlbumId);
}

public sealed record WebsiteAlbumOptionResponse(Guid Id, string Title, DateOnly? AlbumDate, int PhotoCount);

public sealed record WebsitePageSummaryResponse(Guid Id, string Slug, string Title, bool IsPublished, int SortOrder, DateTime UpdatedAt, WebsiteMenu Menu);

public sealed record WebsitePageResponse(
    Guid Id, string Slug, string Title, string? Intro, string Body, string? ImageUrl, bool IsPublished, int SortOrder, WebsiteMenu Menu, Guid? PhotoAlbumId);

public sealed record CommitteeRequest([Required, StringLength(100, MinimumLength = 2)] string Name, int SortOrder = 0);

public sealed record CommitteeResponse(int Id, string Name, string Slug, int SortOrder, IReadOnlyList<CommitteeMemberResponse> Members);

public sealed record CommitteeMemberRequest(
    int CommitteeId,
    Guid? MemberId,
    [Required, StringLength(150, MinimumLength = 2)] string Name,
    [StringLength(100)] string? Function,
    [StringLength(300)] string? Photo)
{
    public CommitteeMemberInput ToInput() => new(CommitteeId, MemberId, Name, Function, Photo);
}

public sealed record CommitteeMemberResponse(
    Guid Id, int CommitteeId, Guid? MemberId, string? MemberNumber, string Name, string? Function, string? PhotoUrl, int SortOrder);

public sealed record KaderCandidateResponse(Guid Id, string MemberNumber, string FullName, string? City);

public sealed record KaderOrderRequest([Required] IReadOnlyList<Guid> Ids);

public sealed record CreatedIntResponse(int Id);

public sealed record PrinceRequest(
    PrinceKind Kind,
    [Range(1958, 2100)] int Year,
    [Required, StringLength(100, MinimumLength = 2)] string PrinceName,
    [StringLength(150)] string? Name,
    [StringLength(500)] string? Motto,
    [StringLength(300)] string? Photo)
{
    public PrinceInput ToInput() => new(Kind, Year, PrinceName, Name, Motto, Photo);
}

public sealed record PrinceResponse(Guid Id, PrinceKind Kind, int Year, string PrinceName, string? Name, string? Motto, string? PhotoUrl);

public sealed record AwardRequest(
    AwardType Type,
    [Range(1958, 2100)] int Year,
    [Required, StringLength(150, MinimumLength = 2)] string Recipient,
    [StringLength(50000)] string? Body,
    [StringLength(300)] string? Photo,
    bool IsPublished)
{
    public AwardInput ToInput() => new(Type, Year, Recipient, Body, Photo, IsPublished);
}

public sealed record AwardResponse(Guid Id, AwardType Type, int Year, string Recipient, string? Body, string? PhotoUrl, string Slug, bool IsPublished);
