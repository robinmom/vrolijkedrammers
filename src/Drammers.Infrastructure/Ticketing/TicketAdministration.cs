using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Tickets;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Ticketing;

public sealed record TicketSummary(
    Guid Id, Guid MemberId, string MemberName, string? MemberNumber, bool MembershipActive, TicketStatus Status, string? BlockedReason,
    int CredentialVersion, string? BoundDeviceName, string? DeviceSecurityLevel, DateTime? BoundAt, int RebindCount, DateTime CreatedAt);

public enum TicketAction
{
    Block,
    Unblock,
    Reissue,
    ResetRebinds,
}

/// <summary>
/// Ticketbeheer voor het bestuur (fase 13, <c>ticket.read</c>/<c>ticket.manage</c>): overzicht, tickets uitgeven voor
/// alle actieve leden, blokkeren/deblokkeren, heruitgeven (<c>credential_version++</c>, oude codes direct ongeldig) en
/// het aantal keer overzetten naar een ander toestel terugzetten. Alles wordt geaudit.
/// </summary>
public sealed class TicketAdministration(DrammersDbContext db, MemberTickets tickets, IAuditLogger audit, IClock clock)
{
    private async Task<int> ActiveYearAsync(CancellationToken cancellationToken) =>
        await db.CarnivalYears.Where(y => y.Active).Select(y => (int?)y.Id).SingleOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.NotFound, "Er is geen actief carnavalsjaar.", DomainErrorKind.NotFound);

    public async Task<(IReadOnlyList<TicketSummary> Items, int Total)> SearchAsync(
        string? search, TicketStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var year = await ActiveYearAsync(cancellationToken);
        var query =
            from t in db.Tickets.AsNoTracking()
            where t.CarnivalYearId == year
            join m in db.Members.AsNoTracking() on t.MemberId equals m.Id
            join d in db.Devices.AsNoTracking() on t.BoundDeviceId equals d.Id into ds
            from d in ds.DefaultIfEmpty()
            select new { t, m, d };
        if (status is { } s)
        {
            query = query.Where(x => x.t.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.m.FullName.Contains(term) || x.m.MemberNumber == term);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.m.FullName).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new TicketSummary(
                x.t.Id, x.m.Id, x.m.FullName, x.m.MemberNumber, (x.m.LocalStatusOverride ?? x.m.MembershipStatus) == MembershipStatus.Active, x.t.Status, x.t.BlockedReason,
                x.t.CredentialVersion, x.d == null ? null : x.d.Name, x.d == null ? null : x.d.AttestationStatus, x.t.BoundAt, x.t.RebindCount, x.t.CreatedAt))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    /// <summary>Geeft elk actief lid zonder ticket er een (idempotent); leden krijgen het anders bij het openen van Mijn QR.</summary>
    public async Task<int> IssueForActiveMembersAsync(CancellationToken cancellationToken)
    {
        var year = await ActiveYearAsync(cancellationToken);
        var missing = await db.Members.AsNoTracking()
            .Where(m => (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active && !db.Tickets.Any(t => t.CarnivalYearId == year && t.MemberId == m.Id))
            .Select(m => m.Id).ToListAsync(cancellationToken);
        db.Tickets.AddRange(missing.Select(id => tickets.NewTicket(year, id)));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("tickets.issued", "CarnivalYear", year.ToString(), null, JsonSerializer.Serialize(new { issued = missing.Count })), cancellationToken);
        return missing.Count;
    }

    public async Task ApplyAsync(Guid id, TicketAction action, string? reason, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Ticket niet gevonden.", DomainErrorKind.NotFound);
        var before = JsonSerializer.Serialize(new { status = ticket.Status.ToString(), ticket.CredentialVersion, ticket.RebindCount });
        switch (action)
        {
            case TicketAction.Block:
                if (string.IsNullOrWhiteSpace(reason))
                {
                    throw new DomainException(ErrorCodes.Validation, "Vul een reden in voor het blokkeren.");
                }

                ticket.Status = TicketStatus.Blocked;
                ticket.BlockedReason = reason.Trim()[..Math.Min(reason.Trim().Length, 500)];
                break;
            case TicketAction.Unblock:
                ticket.Status = TicketStatus.Active;
                ticket.BlockedReason = null;
                break;
            case TicketAction.Reissue:
                // Nieuwe credential_version: alle bestaande codes zijn direct ongeldig; het lid koppelt opnieuw (telt niet mee).
                ticket.CredentialVersion++;
                ticket.BoundDeviceId = null;
                ticket.BoundAt = null;
                break;
            case TicketAction.ResetRebinds:
                ticket.RebindCount = 0;
                break;
        }

        ticket.UpdatedAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry($"ticket.{action.ToString().ToLowerInvariant()}", "Ticket", id.ToString(), before,
            JsonSerializer.Serialize(new { status = ticket.Status.ToString(), ticket.CredentialVersion, ticket.RebindCount, reason })), cancellationToken);
    }
}
