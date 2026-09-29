using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Drammers.Api.Authorization;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Notifications;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Notification.Notifications;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>
/// Pushmeldingen opstellen, plannen, annuleren en de historie met statistiek (fase 10, docs/02 §5.3). Toegang met
/// <c>notification.send</c> of <c>notification.send.group</c>; wie alleen het laatste heeft, ziet en verstuurt alleen
/// eigen meldingen aan de eigen groepen.
/// </summary>
[ApiController]
[Route("api/v1/admin/notifications")]
[RequireActiveUser]
public sealed class AdminNotificationsController(
    DrammersDbContext db, NotificationAdministration notifications, NotificationAudienceResolver resolver) : ControllerBase
{
    private UserAccess Sender()
    {
        var user = CurrentUser.Get(HttpContext)!;
        if (!user.Permissions.Contains(Permissions.NotificationSend) && !user.Permissions.Contains(Permissions.NotificationSendGroup))
        {
            throw new DomainException(ErrorCodes.Forbidden, "Je hebt geen recht om meldingen te versturen.", DomainErrorKind.Forbidden);
        }

        return user;
    }

    private IQueryable<Notification> Visible(UserAccess user) =>
        user.Permissions.Contains(Permissions.NotificationSend)
            ? db.Notifications.AsNoTracking()
            : db.Notifications.AsNoTracking().Where(n => n.SenderUserId == user.UserId);

    [HttpGet]
    [ProducesResponseType<PagedResult<NotificationSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<NotificationSummaryResponse>> Search([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<NotificationSummaryResponse>.Normalize(page, pageSize);
        var query = Visible(Sender());
        var total = await query.CountAsync(cancellationToken);
        var items = await Summaries(query.OrderByDescending(n => n.ScheduledAt ?? n.CreatedAt).Skip((p - 1) * size).Take(size))
            .ToListAsync(cancellationToken);
        return new PagedResult<NotificationSummaryResponse>(items, p, size, total);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<NotificationDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<NotificationDetailResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var query = Visible(Sender()).Where(n => n.Id == id);
        var summary = await Summaries(query).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotificationNotFound, "Melding niet gevonden.", DomainErrorKind.NotFound);
        var n = await query.SingleAsync(cancellationToken);
        var audience = JsonSerializer.Deserialize<NotificationAudience>(n.AudienceJson, NotificationAdministration.Json)!;
        var statuses = await db.NotificationRecipients.AsNoTracking().Where(r => r.NotificationId == id)
            .GroupBy(r => r.DeliveryStatus).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return new NotificationDetailResponse(summary, n.Body, n.DeepLink, audience, await AudienceLabelsAsync(audience, cancellationToken),
            n.SourceType, n.SourceId, statuses.GetValueOrDefault(DeliveryStatus.OptedOut), statuses.GetValueOrDefault(DeliveryStatus.NoDevice));
    }

    /// <summary>Keuzelijsten voor de doelgroep: rollen en groepen (met alleen het groepsrecht: de eigen groepen, geen rollen).</summary>
    [HttpGet("audience-options")]
    [ProducesResponseType<NotificationAudienceOptionsResponse>(StatusCodes.Status200OK)]
    public async Task<NotificationAudienceOptionsResponse> AudienceOptions(CancellationToken cancellationToken)
    {
        var user = Sender();
        var full = user.Permissions.Contains(Permissions.NotificationSend);
        var roles = full
            ? await db.Roles.AsNoTracking().OrderBy(r => r.SortOrder).Select(r => new AudienceRoleOption(r.Code, r.Name)).ToListAsync(cancellationToken)
            : [];
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ownGroups = user.MemberId is { } memberId
            ? await db.GroupMemberships.AsNoTracking()
                .Where(gm => gm.MemberId == memberId && (gm.ValidFrom == null || gm.ValidFrom <= today) && (gm.ValidTo == null || gm.ValidTo >= today))
                .Select(gm => gm.GroupId).ToListAsync(cancellationToken)
            : [];
        var groups = await db.Groups.AsNoTracking().Where(g => g.Active && (full || ownGroups.Contains(g.Id))).OrderBy(g => g.Name)
            .Select(g => new AudienceOptionResponse(g.Id, g.Name)).ToListAsync(cancellationToken);
        return new NotificationAudienceOptionsResponse(full, user.Permissions.Contains(Permissions.NotificationSendUrgent), roles, groups);
    }

    [HttpPost("preview-audience")]
    [ProducesResponseType<AudiencePreview>(StatusCodes.Status200OK)]
    public async Task<AudiencePreview> Preview(AudiencePreviewRequest request, CancellationToken cancellationToken)
    {
        Sender();
        return await resolver.PreviewAsync(request.Audience, request.Category, cancellationToken);
    }

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Create(CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        var notification = await notifications.CreateAsync(
            new NotificationDraft(request.Title, request.Body, request.Category, request.Audience, request.DeepLink, request.ScheduledAt?.ToUniversalTime()),
            Sender(), cancellationToken);
        return Created($"/api/v1/admin/notifications/{notification.Id}", new CreatedResponse(notification.Id));
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var user = Sender();
        if (!await Visible(user).AnyAsync(n => n.Id == id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.NotificationNotFound, "Melding niet gevonden.", DomainErrorKind.NotFound);
        }

        await notifications.CancelAsync(id, user.UserId, cancellationToken);
        return NoContent();
    }

    private IQueryable<NotificationSummaryResponse> Summaries(IQueryable<Notification> query) =>
        query.GroupJoin(db.Users, n => n.SenderUserId, u => u.Id, (n, us) => new { n, us })
            .SelectMany(x => x.us.DefaultIfEmpty(), (x, u) => new NotificationSummaryResponse(
                x.n.Id, x.n.Title, x.n.Category, x.n.Status, x.n.CreatedAt, x.n.ScheduledAt, x.n.SentAt,
                u == null ? null : u.DisplayName, x.n.SourceType,
                x.n.RecipientCount, x.n.PushCount, x.n.DeliveredCount, x.n.FailedCount, x.n.ReadCount));

    private async Task<IReadOnlyList<string>> AudienceLabelsAsync(NotificationAudience audience, CancellationToken cancellationToken)
    {
        var labels = new List<string>();
        if (audience.Everyone)
        {
            labels.Add("Iedereen (ook gasten)");
        }

        if (audience.Members)
        {
            labels.Add("Alle leden");
        }

        if (audience.Dansgarde)
        {
            labels.Add("Dansgarde (met ouders)");
        }

        var roles = audience.Roles ?? [];
        labels.AddRange(await db.Roles.AsNoTracking().Where(r => roles.Contains(r.Code)).OrderBy(r => r.SortOrder).Select(r => "Rol: " + r.Name).ToListAsync(cancellationToken));
        var groups = audience.Groups ?? [];
        labels.AddRange(await db.Groups.AsNoTracking().Where(g => groups.Contains(g.Id)).OrderBy(g => g.Name).Select(g => "Groep: " + g.Name).ToListAsync(cancellationToken));
        var members = audience.MemberIds ?? [];
        labels.AddRange(await db.Members.AsNoTracking().Where(m => members.Contains(m.Id)).OrderBy(m => m.FullName).Select(m => "Lid: " + m.FullName).ToListAsync(cancellationToken));
        return labels;
    }
}

public sealed record NotificationSummaryResponse(
    Guid Id, string Title, NotificationCategory Category, NotificationStatus Status, DateTime CreatedAt, DateTime? ScheduledAt, DateTime? SentAt,
    string? SenderName, string? SourceType, int RecipientCount, int PushCount, int DeliveredCount, int FailedCount, int ReadCount);

public sealed record NotificationDetailResponse(
    NotificationSummaryResponse Summary, string Body, string? DeepLink, NotificationAudience Audience, IReadOnlyList<string> AudienceLabels,
    string? SourceType, Guid? SourceId, int OptedOut, int NoDevice);

public sealed record AudienceRoleOption(string Code, string Name);

/// <param name="AnyAudience">Mag naar elke doelgroep (<c>notification.send</c>); anders alleen de eigen groepen.</param>
/// <param name="Urgent">Mag Dringend en Iedereen gebruiken.</param>
/// <param name="Roles">Rollen als doelgroep (leeg met alleen het groepsrecht).</param>
/// <param name="Groups">Groepen als doelgroep.</param>
public sealed record NotificationAudienceOptionsResponse(
    bool AnyAudience, bool Urgent, IReadOnlyList<AudienceRoleOption> Roles, IReadOnlyList<AudienceOptionResponse> Groups);

public sealed record AudiencePreviewRequest([Required] NotificationAudience Audience, NotificationCategory Category);

public sealed record CreateNotificationRequest(
    [Required, StringLength(Notification.TitleMaxLength, MinimumLength = 1)] string Title,
    [Required, StringLength(Notification.BodyMaxLength, MinimumLength = 1)] string Body,
    NotificationCategory Category,
    [Required] NotificationAudience Audience,
    [StringLength(200)] string? DeepLink,
    DateTime? ScheduledAt);
