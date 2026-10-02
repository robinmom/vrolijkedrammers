using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Contact;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Drammers.Api.Controllers;

/// <summary>
/// Contactformulier van de website (fase 21i): openbaar, met rate limit per IP, een verborgen veld, een minimale invultijd
/// en (als de sleutels zijn gezet) Cloudflare Turnstile. Een als spam herkend bericht geeft hetzelfde antwoord als een
/// verstuurd bericht.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/contact")]
public sealed class ContactController(ContactForm form, IOptions<ContactOptions> options, IOptions<TurnstileOptions> turnstile) : ControllerBase
{
    /// <summary>Ontvangers (zonder adres) en de openbare Turnstile-sleutel als die aan staat.</summary>
    [HttpGet]
    [ProducesResponseType<ContactConfigResponse>(StatusCodes.Status200OK)]
    public ContactConfigResponse Get() => new(
        [.. options.Value.Recipients.Select(r => new ContactRecipientResponse(r.Key.ToLowerInvariant(), r.Value.Label))],
        turnstile.Value.Enabled ? turnstile.Value.SiteKey : null);

    [HttpPost]
    [EnableRateLimiting(AuthorizationSetup.AnonymousFormsPolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Send(ContactMessageRequest request, CancellationToken cancellationToken)
    {
        await form.SendAsync(
            new ContactMessageInput(request.Recipient, request.Name, request.Email, request.Phone, request.Message, request.Website,
                request.ElapsedMs, request.TurnstileToken),
            HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return Accepted();
    }
}

public sealed record ContactRecipientResponse(string Key, string Label);

public sealed record ContactConfigResponse(IReadOnlyList<ContactRecipientResponse> Recipients, string? TurnstileSiteKey);

/// <summary><c>Website</c> is het verborgen veld voor bots; <c>ElapsedMs</c> de tijd sinds het openen van het formulier.</summary>
public sealed record ContactMessageRequest(
    [param: Required, StringLength(30)] string Recipient,
    [param: Required, StringLength(100)] string Name,
    [param: Required, EmailAddress, StringLength(254)] string Email,
    [param: StringLength(30)] string? Phone,
    [param: Required, StringLength(5000, MinimumLength = 2)] string Message,
    [param: StringLength(200)] string? Website,
    int? ElapsedMs,
    [param: StringLength(4096)] string? TurnstileToken);
