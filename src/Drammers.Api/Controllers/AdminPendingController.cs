using Drammers.Api.Authorization;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Festival;
using Drammers.Modules.Membership.Applications;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>
/// Openstaande aanmeldingen voor het dashboard (2026-10-15): leden, optocht en Dansgardefestival, elk alleen met het
/// bijbehorende recht. "Nieuw" wacht op een eerste beoordeling; "Aanvulling ontvangen" is een beantwoorde vraag om meer
/// informatie of een wijziging die opnieuw beoordeeld moet worden.
/// </summary>
[ApiController]
[Route("api/v1/admin/dashboard/pending")]
[RequireActiveUser]
public sealed class AdminPendingController(DrammersDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PendingResponse>(StatusCodes.Status200OK)]
    public async Task<PendingResponse> Get(CancellationToken cancellationToken)
    {
        var permissions = CurrentUser.Get(HttpContext)!.Permissions;
        PendingCount? members = null, parade = null, festival = null;

        if (permissions.Contains(Permissions.MemberApprove))
        {
            members = new PendingCount(
                await db.MembershipApplications.CountAsync(a => a.Status == ApplicationStatus.Submitted, cancellationToken),
                await db.MembershipApplications.CountAsync(a => a.Status == ApplicationStatus.InReview, cancellationToken),
                0);
        }

        if (permissions.Contains(Permissions.ParadeRead))
        {
            var current = db.ParadeRegistrations.Where(r => db.Parades.Any(p => p.Id == r.ParadeId && db.CarnivalYears.Any(y => y.Id == p.CarnivalYearId && y.Active)));
            var supplements = await current.CountAsync(r => r.Status == RegistrationStatus.UnderReview
                && db.ParadeStatusHistory.Where(h => h.RegistrationId == r.Id).OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id)
                    .Select(h => h.Reason).FirstOrDefault() == ParadeReview.SupplementReason, cancellationToken);
            parade = new PendingCount(
                await current.CountAsync(r => r.Status == RegistrationStatus.Submitted, cancellationToken),
                await current.CountAsync(r => r.Status == RegistrationStatus.UnderReview, cancellationToken) - supplements,
                supplements);
        }

        if (permissions.Contains(Permissions.FestivalManage))
        {
            var current = db.DanceFestivalRegistrations.Where(r => db.DanceFestivals.Any(f => f.Id == r.FestivalId && f.Active));
            festival = new PendingCount(
                await current.CountAsync(r => r.Entries.Any(e => e.RemovedAt == null && e.Status == DanceEntryStatus.New), cancellationToken),
                0,
                await current.CountAsync(r => r.Entries.Any(e => e.RemovedAt == null && e.Status == DanceEntryStatus.Changed), cancellationToken));
        }

        return new PendingResponse(members, parade, festival);
    }
}

/// <param name="New">Nieuw, nog niet beoordeeld.</param>
/// <param name="InReview">In behandeling genomen, nog geen besluit.</param>
/// <param name="SupplementReceived">Aanvulling ontvangen of gewijzigd: opnieuw beoordelen.</param>
public sealed record PendingCount(int New, int InReview, int SupplementReceived);

/// <summary>Per onderdeel <c>null</c> als de gebruiker er geen recht voor heeft.</summary>
public sealed record PendingResponse(PendingCount? Members, PendingCount? Parade, PendingCount? Festival);
