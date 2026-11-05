using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Provisioning;
using Drammers.Modules.Membership.Applications;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Aanmeldingen beoordelen (fase 9b): altijd handmatig, met het recht <c>member.approve</c>.</summary>
[ApiController]
[Route("api/v1/admin/membership-applications")]
[RequirePermission(Permissions.MemberApprove)]
public sealed class AdminMembershipApplicationsController(DrammersDbContext db, MembershipApplications applications, IClock clock) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ApplicationSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<ApplicationSummaryResponse>> Search(
        [FromQuery] ApplicationStatus? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<ApplicationSummaryResponse>.Normalize(page, pageSize);
        // Concepten (e-mail nog niet bevestigd) zijn nog geen aanmelding en blijven buiten beeld.
        var query = db.MembershipApplications.AsNoTracking()
            .Where(a => a.Status != ApplicationStatus.Draft && (status == null || a.Status == status));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(a => a.SubmittedAt).Skip((p - 1) * size).Take(size).ToListAsync(cancellationToken);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return new PagedResult<ApplicationSummaryResponse>(
            [.. rows.Select(a => new ApplicationSummaryResponse(a.Id, a.FullName, a.City, a.AgeOn(today), a.IsMinorOn(today), a.Status, a.Source, a.SubmittedAt, a.MembershipType, a.SplitFromMemberId != null))],
            p, size, total);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ApplicationDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ApplicationDetailResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var a = await db.MembershipApplications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Status != ApplicationStatus.Draft, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ApplicationNotFound, "Aanmelding niet gevonden.", DomainErrorKind.NotFound);
        var provisioning = a.ProvisioningId is { } pid
            ? await db.AccountProvisioning.AsNoTracking().Where(p => p.Id == pid)
                .Select(p => new ApplicationProvisioningResponse(p.Id, p.Step, p.MemberNumber, p.Attempts, p.LastError)).SingleOrDefaultAsync(cancellationToken)
            : null;
        var handler = a.HandledBy is { } userId
            ? await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken)
            : null;
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var emailInUseBy = !a.IsMinorOn(today) && a.Status is ApplicationStatus.Submitted or ApplicationStatus.InReview
            ? await applications.EmailInUseAsync(a.Email, cancellationToken)
            : null;
        var splitFrom = a.SplitFromMemberId is { } mainId
            ? await db.Members.AsNoTracking().Where(m => m.Id == mainId)
                .Select(m => new ApplicationSplitResponse(m.Id, m.MemberNumber, m.FullName)).SingleOrDefaultAsync(cancellationToken)
            : null;
        // Het eerste jaar contributie: alleen te betalen als de aanvraag tussen de incasso en carnaval binnenkwam.
        var aangevraagdOp = DateOnly.FromDateTime(a.SubmittedAt ?? a.CreatedAt);
        var betaalplicht = a.PaymentRequestedAt is not null || await applications.NeedsFirstYearPaymentAsync(aangevraagdOp, cancellationToken);
        var payment = betaalplicht
            ? new ApplicationPaymentResponse(
                true,
                a.PaymentAmountCents is { } cents ? cents / 100m : await applications.FirstYearAmountAsync(a, aangevraagdOp, cancellationToken),
                a.PaymentRequestedAt, a.PaymentExpiresAt, a.PaidAt, a.PaymentSendCount, a.PaymentCheckoutUrl)
            : null;
        return new ApplicationDetailResponse(
            a.Id, a.Status, a.Source, a.FirstName, a.NamePrefix, a.LastName, a.FullName, a.Gender, a.BirthDate, a.AgeOn(today), a.IsMinorOn(today),
            a.AddressLine, a.PostalCode, a.City, a.Email, a.Phone, a.GuardianName, a.GuardianPhone,
            MembershipApplications.MaskIban(a.Iban), a.AccountHolder, a.MandateReference, a.MandateConsentAt, a.ConsentPrivacyAt, a.ConsentPhoto,
            a.SubmittedAt, handler, a.HandledAt, a.DecisionAt, a.RejectionReason, a.InternalNotes, a.ResultingMemberId, provisioning, emailInUseBy,
            a.MembershipType, splitFrom, payment);
    }

    [HttpPost("{id:guid}/start-review")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartReview(Guid id, CancellationToken cancellationToken)
    {
        await applications.StartReviewAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken cancellationToken)
    {
        await applications.ApproveAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Betaalverzoek voor het eerste jaar contributie sturen. Alleen nodig als iemand lid wordt ná de incassodatum en
    /// vóór carnaval; dan valt hij buiten de inning van dit jaar. De aanvraag blijft in behandeling tot er betaald is
    /// en wordt daarna vanzelf goedgekeurd. Nog een keer aanroepen stuurt dezelfde betaallink opnieuw.
    /// </summary>
    [HttpPost("{id:guid}/payment-request")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SendPaymentRequest(Guid id, CancellationToken cancellationToken)
    {
        await applications.SendPaymentRequestAsync(id, Request.PublicBaseUrlFor(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Reject(Guid id, RejectApplicationRequest request, CancellationToken cancellationToken)
    {
        await applications.RejectAsync(id, request.Reason, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/notes")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetNotes(Guid id, ApplicationNotesRequest request, CancellationToken cancellationToken)
    {
        await applications.SetNotesAsync(id, request.Notes, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        await applications.RetryAsync(id, cancellationToken);
        return NoContent();
    }
}

public sealed record ApplicationSummaryResponse(
    Guid Id, string FullName, string City, int Age, bool Minor, ApplicationStatus Status, ApplicationSource Source, DateTime? SubmittedAt,
    MembershipType MembershipType, bool Split = false);

public sealed record ApplicationProvisioningResponse(Guid Id, ProvisioningStep Step, string? MemberNumber, int Attempts, string? LastError);

public sealed record ApplicationDetailResponse(
    Guid Id, ApplicationStatus Status, ApplicationSource Source, string FirstName, string? NamePrefix, string LastName, string FullName,
    string? Gender, DateOnly BirthDate, int Age, bool Minor, string AddressLine, string PostalCode, string City, string Email, string? Phone,
    string? GuardianName, string? GuardianPhone, string? IbanMasked, string? AccountHolder, string MandateReference, DateTime? MandateConsentAt,
    DateTime ConsentPrivacyAt, bool ConsentPhoto, DateTime? SubmittedAt, string? HandledBy, DateTime? HandledAt, DateTime? DecisionAt,
    string? RejectionReason, string? InternalNotes, Guid? ResultingMemberId, ApplicationProvisioningResponse? Provisioning,
    string? EmailInUseBy, MembershipType MembershipType, ApplicationSplitResponse? SplitFrom = null,
    ApplicationPaymentResponse? Payment = null);

/// <summary>
/// Het eerste jaar contributie (wens 2026-11-05). <c>Required</c> is waar als deze aanvraag na de incassodatum en vóór
/// carnaval binnenkwam: dan toont het portal de knop om een betaalverzoek te sturen. Is er al een verzoek verstuurd,
/// dan staan hier ook het bedrag, de vervaldatum en of er betaald is.
/// </summary>
public sealed record ApplicationPaymentResponse(
    bool Required, decimal Amount, DateTime? RequestedAt, DateTime? ExpiresAt, DateTime? PaidAt, int SendCount, string? CheckoutUrl);

/// <summary>Lid splitsen (fase 25): het hoofdlid van wie dit het tweede lid is.</summary>
public sealed record ApplicationSplitResponse(Guid MemberId, string MemberNumber, string FullName);

public sealed record RejectApplicationRequest([param: Required, StringLength(500, MinimumLength = 3)] string Reason);

public sealed record ApplicationNotesRequest([param: StringLength(2000)] string? Notes);
