using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Contact;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Festival;
using Drammers.Modules.Content.Festival;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Drammers.Api.Controllers;

/// <summary>
/// Dansgardefestival op de website (2026-10-12): informatie, documenten, inschrijven (Turnstile, verborgen veld, minimale
/// invultijd) en de eigen inschrijving bekijken, wijzigen en betalen via de link uit de mail.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/dance-festival")]
public sealed class DanceFestivalController(DanceFestivals festivals, IOptions<TurnstileOptions> turnstile) : ControllerBase
{
    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    [HttpGet]
    [ProducesResponseType<DanceFestivalPublicResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DanceFestivalPublicResponse>> Get(CancellationToken cancellationToken) =>
        await festivals.PublicAsync(cancellationToken) is { } view
            ? new DanceFestivalPublicResponse(view, turnstile.Value.Enabled ? turnstile.Value.SiteKey : null)
            : throw new DomainException(ErrorCodes.NotFound, "Er is geen Dansgardefestival.", DomainErrorKind.NotFound);

    /// <summary>Reglement of privacyverklaring (PDF).</summary>
    [HttpGet("documents/{kind:regex(^(reglement|privacyverklaring)$)}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Document(string kind, CancellationToken cancellationToken)
    {
        var stream = await festivals.OpenDocumentAsync(kind, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Document niet gevonden.", DomainErrorKind.NotFound);
        Response.Headers.CacheControl = "public, max-age=300";
        return File(stream, "application/pdf", $"Dansgardefestival {kind}.pdf");
    }

    [HttpPost("registrations")]
    [EnableRateLimiting(AuthorizationSetup.AnonymousFormsPolicy)]
    [ProducesResponseType<DanceFestivalSubmittedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Submit(DanceFestivalRegistrationRequest request, CancellationToken cancellationToken)
    {
        var number = await festivals.SubmitAsync(
            request.ToInput(), request.Website, request.ElapsedMs, request.TurnstileToken, HttpContext.Connection.RemoteIpAddress?.ToString(), BaseUrl,
            cancellationToken);
        return Accepted(new DanceFestivalSubmittedResponse(number));
    }

    [HttpGet("registrations/{id:guid}")]
    [EnableRateLimiting(AuthorizationSetup.AnonymousStatusPolicy)]
    [ProducesResponseType<FestivalOwnRegistrationView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<FestivalOwnRegistrationView> Own(Guid id, [FromQuery, Required] string t, CancellationToken cancellationToken) =>
        festivals.OwnAsync(id, t, cancellationToken);

    [HttpPut("registrations/{id:guid}")]
    [EnableRateLimiting(AuthorizationSetup.AnonymousFormsPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Update(Guid id, [FromQuery, Required] string t, DanceFestivalRegistrationRequest request, CancellationToken cancellationToken)
    {
        await festivals.UpdateOwnAsync(id, t, request.ToInput(), cancellationToken);
        return NoContent();
    }

    /// <summary>De betaallink uit de mail: stuurt door naar een nieuwe iDEAL-betaling bij Mollie.</summary>
    [HttpGet("payments/{id:guid}/pay")]
    [EnableRateLimiting(AuthorizationSetup.AnonymousStatusPolicy)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Pay(Guid id, [FromQuery, Required] string t, CancellationToken cancellationToken)
    {
        try
        {
            return Redirect(await festivals.PayAsync(id, t, BaseUrl, cancellationToken));
        }
        catch (DomainException ex) when (ex.Code is ErrorCodes.OrderNotPayable or ErrorCodes.PaymentFailed or ErrorCodes.PaymentsNotConfigured)
        {
            return Redirect($"{BaseUrl}/dansgardefestival/?melding={Uri.EscapeDataString(ex.Message)}");
        }
    }
}

/// <summary>Beheer van het Dansgardefestival (portal, rol Dansgardefestival en bestuur).</summary>
[ApiController]
[Route("api/v1/admin/dance-festival")]
[RequirePermission(Permissions.FestivalManage)]
public sealed class AdminDanceFestivalController(DanceFestivals festivals) : ControllerBase
{
    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    [HttpGet("settings")]
    [ProducesResponseType<FestivalSettingsView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<FestivalSettingsView>> Settings(CancellationToken cancellationToken) =>
        await festivals.SettingsAsync(cancellationToken) is { } view ? view : NoContent();

    [HttpPut("settings")]
    [ProducesResponseType<FestivalSettingsView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<FestivalSettingsView> SaveSettings(DanceFestivalSettingsRequest request, CancellationToken cancellationToken) =>
        festivals.SaveSettingsAsync(request.ToInput(), cancellationToken);

    [HttpPost("documents/{kind:regex(^(reglement|privacyverklaring)$)}")]
    [RequestSizeLimit(ContentFiles.MaxAttachmentBytes + 1_000_000)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UploadDocument(string kind, IFormFile file, CancellationToken cancellationToken)
    {
        await festivals.SetDocumentAsync(kind, new UploadedFile(file.FileName, file.Length, file.OpenReadStream), cancellationToken);
        return NoContent();
    }

    [HttpGet("registrations")]
    [ProducesResponseType<FestivalAdminList>(StatusCodes.Status200OK)]
    public Task<FestivalAdminList> Registrations(CancellationToken cancellationToken) => festivals.ListAsync(cancellationToken);

    [HttpGet("registrations/{id:guid}")]
    [ProducesResponseType<FestivalRegistrationDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<FestivalRegistrationDetail> Registration(Guid id, CancellationToken cancellationToken) => festivals.DetailAsync(id, cancellationToken);

    /// <summary>Een eerdere versie van de inschrijving (zoals de vereniging die had ingevuld).</summary>
    [HttpGet("registrations/{id:guid}/versions/{version:int}")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Version(Guid id, int version, CancellationToken cancellationToken) =>
        Content(await festivals.VersionAsync(id, version, cancellationToken), "application/json");

    [HttpPost("registrations/{id:guid}/decisions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Decide(Guid id, DanceFestivalDecisionRequest request, CancellationToken cancellationToken)
    {
        await festivals.DecideAsync(id, request.EntryIds, request.Decision, request.Message, BaseUrl, cancellationToken);
        return NoContent();
    }

    /// <summary>Schuiven op de wachtlijst: -1 = omhoog, +1 = omlaag.</summary>
    [HttpPost("entries/{id:guid}/waitlist-move")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MoveWaitlist(Guid id, DanceFestivalWaitlistMoveRequest request, CancellationToken cancellationToken)
    {
        await festivals.MoveWaitlistAsync(id, request.Direction, cancellationToken);
        return NoContent();
    }

    [HttpPost("registrations/{id:guid}/payments")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> RequestPayment(Guid id, DanceFestivalPaymentRequest request, CancellationToken cancellationToken) =>
        Created((string?)null, new CreatedResponse(await festivals.RequestPaymentAsync(id, request.AmountCents, BaseUrl, cancellationToken)));

    [HttpPost("payments/{id:guid}/refresh")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefreshPayment(Guid id, CancellationToken cancellationToken)
    {
        await festivals.RefreshPaymentAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("payments/{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelPayment(Guid id, CancellationToken cancellationToken)
    {
        await festivals.CancelPaymentAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken) =>
        File(await festivals.ExportAsync(cancellationToken), "text/csv", "dansgardefestival-inschrijvingen.csv");
}

public sealed record DanceFestivalPublicResponse(FestivalPublicView Festival, string? TurnstileSiteKey);

public sealed record DanceFestivalSubmittedResponse(int? Number);

public sealed record DanceFestivalEntryRequest(
    Guid? Id,
    DanceType DanceType,
    DanceFormat Format,
    [param: Required, StringLength(300)] string Names,
    [param: Range(2, 99)] decimal AverageAge,
    [param: Range(1, 200)] int DancerCount,
    [param: Required, StringLength(100)] string LeaderName,
    [param: StringLength(150)] string? Title,
    ShowStyle? ShowStyle);

/// <summary><c>Website</c> is het verborgen veld voor bots; <c>ElapsedMs</c> de tijd sinds het openen van het formulier.</summary>
public sealed record DanceFestivalRegistrationRequest(
    [param: Required, StringLength(150)] string ClubName,
    [param: Required, StringLength(100)] string ContactName,
    [param: Required, StringLength(200)] string AddressLine,
    [param: Required, StringLength(10)] string PostalCode,
    [param: Required, StringLength(100)] string City,
    [param: Required, StringLength(30)] string Phone,
    [param: Required, EmailAddress, StringLength(254)] string Email,
    [param: StringLength(2000)] string? Remarks,
    bool? PhotoConsent,
    bool PrivacyAccepted,
    bool RulesAccepted,
    [param: Required, MaxLength(DanceFestivals.MaxEntries)] IReadOnlyList<DanceFestivalEntryRequest> Entries,
    [param: StringLength(200)] string? Website,
    int? ElapsedMs,
    [param: StringLength(4096)] string? TurnstileToken)
{
    public FestivalRegistrationInput ToInput() => new(
        ClubName, ContactName, AddressLine, PostalCode, City, Phone, Email, Remarks, PhotoConsent, PrivacyAccepted, RulesAccepted,
        [.. Entries.Select(e => new FestivalEntryInput(e.Id, e.DanceType, e.Format, e.Names, e.AverageAge, e.DancerCount, e.LeaderName, e.Title, e.ShowStyle))]);
}

public sealed record DanceFestivalSettingsRequest(
    [param: Required, StringLength(100)] string Name,
    DateOnly FestivalDate,
    [param: Required, StringLength(200)] string Location,
    bool PageEnabled,
    DateTime? RegistrationOpensAt,
    DateTime? RegistrationClosesAt,
    [param: Range(0, 1000)] int Capacity,
    bool PaymentsEnabled,
    [param: Range(0, 100_000)] int PriceSoloDuoCents,
    [param: Range(0, 100_000)] int PriceGroupOneDanceCents,
    [param: Range(0, 100_000)] int PriceGroupTwoDancesCents,
    [param: Required, EmailAddress, StringLength(254)] string NotificationEmail)
{
    public FestivalSettingsInput ToInput() => new(
        Name, FestivalDate, Location, PageEnabled, RegistrationOpensAt, RegistrationClosesAt, Capacity, PaymentsEnabled, PriceSoloDuoCents,
        PriceGroupOneDanceCents, PriceGroupTwoDancesCents, NotificationEmail);
}

public sealed record DanceFestivalDecisionRequest(
    [param: Required, MinLength(1)] IReadOnlyList<Guid> EntryIds, FestivalDecision Decision, [param: StringLength(2000)] string? Message);

public sealed record DanceFestivalWaitlistMoveRequest([param: Range(-1, 1)] int Direction);

public sealed record DanceFestivalPaymentRequest([param: Range(1, 1_000_000)] int AmountCents);
