using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Notifications;
using Drammers.Modules.Notification.Notifications;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Eigen meldingen (fase 10): push-token van dit apparaat, inbox met gelezen/ongelezen en voorkeuren per categorie.</summary>
[ApiController]
[Route("api/v1/me")]
[RequireActiveUser]
public sealed class MeNotificationsController(MyNotifications notifications) : ControllerBase
{
    private Guid UserId => CurrentUser.Get(HttpContext)!.UserId;

    /// <summary>Registreert of ververst het Expo-push-token van een eigen, actief apparaat.</summary>
    [HttpPut("devices/{id:guid}/push-token")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetPushToken(Guid id, PushTokenRequest request, CancellationToken cancellationToken)
    {
        await notifications.RegisterDeviceTokenAsync(UserId, id, request.Token, cancellationToken);
        return NoContent();
    }

    /// <summary>De inbox toont ook meldingen als push is uitgezet of geweigerd.</summary>
    [HttpGet("notifications")]
    [RequirePermission(Permissions.NotificationReadOwn)]
    [ProducesResponseType<InboxPage>(StatusCodes.Status200OK)]
    public Task<InboxPage> Inbox([FromQuery] int? page, CancellationToken cancellationToken) =>
        notifications.GetInboxAsync(UserId, page ?? 1, cancellationToken);

    [HttpPost("notifications/{id:guid}/read")]
    [RequirePermission(Permissions.NotificationReadOwn)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        await notifications.MarkReadAsync(UserId, id, cancellationToken);
        return NoContent();
    }

    [HttpPost("notifications/read-all")]
    [RequirePermission(Permissions.NotificationReadOwn)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await notifications.MarkAllReadAsync(UserId, cancellationToken);
        return NoContent();
    }

    [HttpGet("notification-preferences")]
    [ProducesResponseType<IReadOnlyList<CategoryPreference>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<CategoryPreference>> GetPreferences(CancellationToken cancellationToken) =>
        notifications.GetPreferencesAsync(UserId, cancellationToken);

    [HttpPut("notification-preferences")]
    [ProducesResponseType<IReadOnlyList<CategoryPreference>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<IReadOnlyList<CategoryPreference>> SetPreferences(SetPreferencesRequest request, CancellationToken cancellationToken) =>
        notifications.SetPreferencesAsync(UserId, [.. request.Preferences.Select(p => (p.Category, p.Enabled))], cancellationToken);
}

public sealed record PushTokenRequest([Required, StringLength(200, MinimumLength = 20)] string Token);

public sealed record PreferenceChange(NotificationCategory Category, bool Enabled);

public sealed record SetPreferencesRequest([Required] IReadOnlyList<PreferenceChange> Preferences);
