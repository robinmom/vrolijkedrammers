using System.Globalization;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Qr;
using Drammers.Modules.Ticketing.Sales;
using Drammers.Modules.Ticketing.Tickets;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Ticketing;

public sealed record AccessCounts(int Inside, int Scans, int Refused);

public sealed record AccessStatus(AccessEvent? Current, AccessEvent? Next, AccessCounts? Counts);

/// <summary>Wat het deurpersoneel na een scan of inchecken ziet (fase 14, Figma 📷 Toegangscontrole).</summary>
/// <remarks><see cref="Persons"/>: bij een gekochte kaart hoeveel personen tegelijk naar binnen gaan (fase 19c).</remarks>
public sealed record AccessResult(
    Guid? ScanId, AccessOutcome Outcome, string Title, string Message, string? HolderName, DateTime? PreviousAt, bool NeedsDecision,
    AccessCounts Counts, int? Persons = null);

public sealed record AccessHistoryItem(DateTime At, AccessMethod Method, AccessOutcome Outcome, AccessDecision? Decision, string EventTitle, string? Operator);

/// <summary>Controlelijst voor offline scannen (fase 15, lichte variant): alleen in het geheugen van de scanner.</summary>
public sealed record OfflineTicket(
    string Ref, int CredentialVersion, bool Blocked, bool MembershipActive, string? DeviceShortId, string? DevicePublicKey, string HolderName);

public sealed record OfflinePack(
    DateTime GeneratedAt, AccessEvent? Current, DateTime? ValidFrom, DateTime? ValidTo, IReadOnlyList<string> ServerKeys,
    IReadOnlyList<OfflineTicket> Tickets);

/// <summary>Een offline scan uit de wachtrij van de scanner; <see cref="ScannedAt"/> is de tijd op het toestel.</summary>
public sealed record OfflineScan(Guid ClientScanId, string Code, DateTime ScannedAt, AccessOutcome LocalOutcome);

public sealed record OfflineSyncResult(int Accepted, int Duplicates, int Skipped, int Conflicts);

public sealed record MemberAccess(AccessEvent? Current, bool Inside, DateTime? InsideSince, string? TicketProblem, IReadOnlyList<AccessHistoryItem> History);

/// <summary>
/// Toegangscontrole bij de deur (fase 14): QR-codes scannen in de app en leden inchecken in het portal, in één
/// toegangslog per toegangsmoment: een activiteit met toegangscontrole, of anders de carnavalsdag
/// (<see cref="AccessWindows"/>; QR en scannen volgen dezelfde regels). Groen bij de eerste keer (of opnieuw op hetzelfde toestel), oranje
/// als het lid al via een ander toestel of handmatig binnen is (het deurpersoneel beslist), rood met de reden.
/// Geen bandjes (OQ-21); wie de rol Deurcontrole heeft mag scannen (OQ-73).
/// </summary>
public sealed class DoorAccess(DrammersDbContext db, TicketValidation validation, AccessWindows windows, TicketSigningKeys keys, IClock clock)
{
    private static readonly TimeZoneInfo Loil = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private DateTime Now => clock.UtcNow.UtcDateTime;

    public async Task<AccessStatus> StatusAsync(CancellationToken cancellationToken)
    {
        var current = await windows.CurrentAsync(cancellationToken);
        var next = current is null ? await windows.NextAsync(cancellationToken) : null;
        return new AccessStatus(current, next, current is null ? null : await CountsAsync(current, cancellationToken));
    }

    private async Task<AccessEvent> RequireCurrentAsync(CancellationToken cancellationToken) =>
        await windows.CurrentAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.AccessNotActive, "Er is nu geen carnaval en geen activiteit met toegangscontrole.", DomainErrorKind.Conflict);

    /// <summary>Scans van één toegangsmoment: een activiteit of een carnavalsdag.</summary>
    private IQueryable<AccessScan> ScansOf(AccessEvent access) =>
        access.EventId is { } id
            ? db.AccessScans.AsNoTracking().Where(s => s.EventId == id)
            : db.AccessScans.AsNoTracking().Where(s => s.EventId == null && s.CarnivalDay == access.CarnivalDay);

    public async Task<AccessCounts> CountsAsync(AccessEvent access, CancellationToken cancellationToken)
    {
        var scans = await ScansOf(access).Select(s => new { s.MemberId, s.Outcome, s.Decision, s.Persons }).ToListAsync(cancellationToken);
        var admitted = scans.Where(s => s.Outcome is AccessOutcome.Admitted or AccessOutcome.AdmittedAgain
            || (s.Outcome == AccessOutcome.Warning && s.Decision == AccessDecision.Admitted)).ToList();
        // Leden één keer; gekochte kaarten (zonder lid) met het aantal personen op de QR.
        var inside = admitted.Where(s => s.MemberId is not null).Select(s => s.MemberId).Distinct().Count()
            + admitted.Where(s => s.MemberId is null).Sum(s => s.Persons ?? 0);
        var refused = scans.Count(s => s.Outcome == AccessOutcome.Refused || s.Decision == AccessDecision.Refused);
        return new AccessCounts(inside, scans.Count, refused);
    }

    /// <summary>De eerste toelating van dit lid bij dit toegangsmoment (vóór <paramref name="before"/>, voor offline scans).</summary>
    private async Task<AccessScan?> LastAdmissionAsync(AccessEvent access, Guid memberId, CancellationToken cancellationToken, DateTime? before = null) =>
        (await ScansOf(access).Where(s => s.MemberId == memberId && (before == null || s.ScannedAt <= before))
            .OrderBy(s => s.ScannedAt).ToListAsync(cancellationToken)).FirstOrDefault(s => s.Admits);

    private static AccessScan NewScan(AccessEvent access) => new()
    {
        Id = IdGenerator.NewId(),
        EventId = access.EventId,
        CarnivalDay = access.EventId is null ? access.CarnivalDay : null,
    };

    private static string Time(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Loil).ToString("HH:mm", CultureInfo.InvariantCulture);

    private async Task<string> WhoAsync(AccessScan scan, bool details, CancellationToken cancellationToken)
    {
        if (!details)
        {
            return "";
        }

        var user = await db.Users.AsNoTracking().Where(u => u.Id == scan.OperatorUserId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken);
        var device = scan.OperatorDeviceId is { } d ? await db.Devices.AsNoTracking().Where(x => x.Id == d).Select(x => x.Name).SingleOrDefaultAsync(cancellationToken) : null;
        return $" ({string.Join(", ", new[] { user, device }.Where(x => !string.IsNullOrWhiteSpace(x)))})";
    }

    /// <summary>Scan in de app. <paramref name="details"/> = het deurpersoneel mag zien wie eerder scande (<c>ticket.scan.details</c>).</summary>
    public async Task<AccessResult> ScanAsync(Guid operatorId, string? installationId, string code, bool details, CancellationToken cancellationToken)
    {
        var accessEvent = await RequireCurrentAsync(cancellationToken);
        var device = await OperatorDeviceAsync(operatorId, installationId, cancellationToken);
        var (scan, title, message, holder, previousAt) = await EvaluateAsync(operatorId, device, code, accessEvent, Now, details, cancellationToken);
        db.AccessScans.Add(scan);
        await db.SaveChangesAsync(cancellationToken);
        return new AccessResult(scan.Id, scan.Outcome, title, message, holder, previousAt, scan.Outcome == AccessOutcome.Warning,
            await CountsAsync(accessEvent, cancellationToken), scan.OrderTicketId is null ? null : scan.Persons);
    }

    private async Task<Device?> OperatorDeviceAsync(Guid operatorId, string? installationId, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(installationId)
            ? null
            : await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.UserId == operatorId && d.InstallationId == installationId && d.Status == DeviceStatus.Active, cancellationToken);

    /// <summary>Beoordeelt een QR-scan op moment <paramref name="at"/> (online nu, offline de tijd op het toestel).</summary>
    private async Task<(AccessScan Scan, string Title, string Message, string? Holder, DateTime? PreviousAt)> EvaluateAsync(
        Guid operatorId, Device? device, string code, AccessEvent accessEvent, DateTime at, bool details, CancellationToken cancellationToken,
        bool fromQueue = false)
    {
        if (QrPayload.TryDecode(code) is { Version: QrPayload.OrderTicket } purchased)
        {
            return await EvaluatePurchasedAsync(operatorId, device, purchased, accessEvent, at, fromQueue, cancellationToken);
        }

        var check = await validation.ValidateAsync(code, cancellationToken, new DateTimeOffset(at, TimeSpan.Zero));
        var payload = QrPayload.TryDecode(code);
        var ticket = payload is null ? null : await db.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.PublicRef == payload.Ref, cancellationToken);
        var scan = NewScan(accessEvent);
        scan.TicketId = ticket?.Id;
        scan.MemberId = ticket?.MemberId;
        scan.Method = AccessMethod.Qr;
        scan.OperatorUserId = operatorId;
        scan.OperatorDeviceId = device?.Id;
        scan.ScannedAt = at;

        string title, message;
        DateTime? previousAt = null;
        if (!check.IsValid)
        {
            (scan.Outcome, scan.Reason, title, message) = (AccessOutcome.Refused, check.Result.ToString(), "Geen toegang", check.Message);
        }
        else
        {
            var previous = await LastAdmissionAsync(accessEvent, ticket!.MemberId, cancellationToken, before: at);
            previousAt = previous?.ScannedAt;
            var offline = previous?.Offline == true ? " (offline)" : "";
            if (previous is null)
            {
                (scan.Outcome, title, message) = (AccessOutcome.Admitted, "Toegang geldig", "Eerste keer vanavond");
            }
            else if (previous.Method == AccessMethod.Qr && device is not null && previous.OperatorDeviceId == device.Id)
            {
                (scan.Outcome, title, message) = (AccessOutcome.AdmittedAgain, "Toegang geldig", $"Al eerder{offline} gescand op dit toestel om {Time(previous.ScannedAt)}");
            }
            else
            {
                scan.Outcome = AccessOutcome.Warning;
                scan.Reason = previous.Method == AccessMethod.Manual ? "CheckedInManually" : "OtherDevice";
                title = "Let op";
                message = previous.Method == AccessMethod.Manual
                    ? $"Vanavond al ingecheckt om {Time(previous.ScannedAt)}{await WhoAsync(previous, details, cancellationToken)}. Controleer of dit dezelfde persoon is."
                    : $"Vanavond al{offline} gescand op een ander toestel om {Time(previous.ScannedAt)}{await WhoAsync(previous, details, cancellationToken)}. Controleer of dit dezelfde persoon is.";
            }
        }

        return (scan, title, message, check.Ticket?.HolderName, previousAt);
    }

    /// <summary>
    /// Gekochte kaart (fase 19c): één QR voor alle kaarten van de bestelling. Alleen online (besluit 30-09-2026): de server
    /// zet de QR in één keer op gebruikt, zodat hij niet op twee toestellen tegelijk werkt. Alle personen gaan tegelijk naar
    /// binnen. De kaart moet bij dit toegangsmoment horen: dezelfde activiteit, of anders dezelfde dag.
    /// </summary>
    private async Task<(AccessScan Scan, string Title, string Message, string? Holder, DateTime? PreviousAt)> EvaluatePurchasedAsync(
        Guid operatorId, Device? device, QrPayload payload, AccessEvent accessEvent, DateTime at, bool offline, CancellationToken cancellationToken)
    {
        var scan = NewScan(accessEvent);
        scan.Method = AccessMethod.Qr;
        scan.OperatorUserId = operatorId;
        scan.OperatorDeviceId = device?.Id;
        scan.ScannedAt = at;
        (AccessScan, string, string, string?, DateTime?) Refuse(string reason, string message, string? holder = null, DateTime? previous = null)
        {
            scan.Outcome = AccessOutcome.Refused;
            scan.Reason = reason;
            return (scan, "Geen toegang", message, holder, previous);
        }

        var serverKeys = await keys.PublicKeysAsync(cancellationToken);
        if (!serverKeys.Any(k => TicketQrValidator.Verify(k, payload.UnsignedBytes(), payload.Signature)))
        {
            return Refuse(nameof(QrCheck.InvalidSignature), "Ongeldige handtekening: de code is nagemaakt of gewijzigd.");
        }

        var row = await (
            from t in db.OrderTickets.AsNoTracking()
            where t.PublicRef == payload.Ref
            join o in db.SaleOrders.AsNoTracking() on t.OrderId equals o.Id
            join p in db.SaleProducts.AsNoTracking() on o.ProductId equals p.Id
            select new { t, o.Status, o.GroupName, o.BuyerName, o.Number, p.Kind, p.Name, p.Date, p.EventId }).SingleOrDefaultAsync(cancellationToken);
        if (row is null || row.Kind == SaleProductKind.Tokens)
        {
            return Refuse(nameof(QrCheck.UnknownTicket), "Onbekende kaart.");
        }

        scan.OrderTicketId = row.t.Id;
        scan.Persons = row.t.Quantity;
        var holder = row.GroupName ?? row.BuyerName;
        if (offline)
        {
            return Refuse("OnlineOnly", "Gekochte kaarten kunnen alleen online gescand worden.", holder);
        }

        if (row.Status != SaleOrderStatus.Confirmed || row.t.Status == OrderTicketStatus.Cancelled)
        {
            return Refuse(nameof(QrCheck.Blocked), $"Bestelling {row.Number} is geannuleerd of niet betaald.", holder);
        }

        var accessDay = accessEvent.CarnivalDay ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(accessEvent.StartAt, Loil));
        var fits = row.EventId is { } eventId ? accessEvent.EventId == eventId : row.Date is not { } date || date == accessDay;
        if (!fits)
        {
            var when = row.Date is { } d ? d.ToString("dddd d MMMM", CultureInfo.GetCultureInfo("nl-NL")) : row.Name;
            return Refuse(nameof(QrCheck.OutsideValidity), $"Deze kaart is voor {row.Name} ({when}), niet voor {accessEvent.Title}.", holder);
        }

        if (row.t.Status == OrderTicketStatus.Used)
        {
            return Refuse("AlreadyUsed", $"Al gescand om {Time(row.t.UsedAt ?? at)}: alle {row.t.Quantity} personen zijn toen naar binnen gegaan.", holder, row.t.UsedAt);
        }

        // In één keer op gebruikt; wie tegelijk op een ander toestel scant, krijgt rood.
        var claimed = await db.OrderTickets.Where(t => t.Id == row.t.Id && t.Status == OrderTicketStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, OrderTicketStatus.Used).SetProperty(t => t.UsedAt, at), cancellationToken);
        if (claimed == 0)
        {
            return Refuse("AlreadyUsed", "Deze QR is net op een ander toestel gescand.", holder);
        }

        scan.Outcome = AccessOutcome.Admitted;
        var persons = row.t.Quantity == 1 ? "1 persoon" : $"{row.t.Quantity} personen";
        return (scan, "Toegang geldig", $"{persons} tegelijk naar binnen · {row.Name}", holder, null);
    }

    /// <summary>
    /// Controlelijst voor offline scannen (lichte variant): per ticket van het actieve carnavalsjaar de referentie,
    /// versie, blokkade, lidmaatschap, de sleutel van het gekoppelde toestel en de naam, plus de publieke sleutel van de
    /// server. De scanner houdt die alleen in het geheugen zolang het scanscherm open is (geen ledengegevens op schijf).
    /// </summary>
    public async Task<OfflinePack> OfflinePackAsync(CancellationToken cancellationToken)
    {
        var current = await windows.CurrentAsync(cancellationToken);
        var year = await windows.ActiveYearAsync(cancellationToken);
        var tickets = year is null
            ? []
            : await (
                from t in db.Tickets.AsNoTracking()
                where t.CarnivalYearId == year.Id
                join m in db.Members.AsNoTracking() on t.MemberId equals m.Id
                join d in db.Devices.AsNoTracking() on t.BoundDeviceId equals d.Id into ds
                from d in ds.DefaultIfEmpty()
                select new { t.PublicRef, t.CredentialVersion, t.Status, MembershipStatus = m.LocalStatusOverride ?? m.MembershipStatus, t.BoundDeviceId, DeviceActive = d != null && d.Status == DeviceStatus.Active, DeviceKey = d == null ? null : d.PublicKey, m.FullName })
                .ToListAsync(cancellationToken);
        var window = current is null ? ((DateTime From, DateTime To)?)null : AccessWindows.Window(current);
        // De serversleutel bestaat pas na de eerste servercode; voor offline controle moet hij er altijd zijn.
        (await keys.ActivePrivateKeyAsync(cancellationToken)).Dispose();

        return new OfflinePack(
            Now, current, window?.From, window?.To,
            [.. (await keys.PublicKeysAsync(cancellationToken)).Select(Convert.ToBase64String)],
            [.. tickets.Select(t => new OfflineTicket(
                Convert.ToBase64String(t.PublicRef), t.CredentialVersion, t.Status == TicketStatus.Blocked, t.MembershipStatus == MembershipStatus.Active,
                t.BoundDeviceId is { } id && t.DeviceActive ? Convert.ToBase64String(QrPayload.ShortDeviceId(id)) : null,
                t.DeviceActive ? t.DeviceKey : null, t.FullName))]);
    }

    /// <summary>
    /// Offline scans uit de wachtrij verwerken (idempotent op <see cref="OfflineScan.ClientScanId"/>): de server controleert
    /// opnieuw op het moment van scannen en legt het vast als offline. Toonde de scanner groen of oranje, maar is de scan
    /// volgens de server ongeldig, dan is dat een offline-conflict (zichtbaar in de toegangslog). Scans ouder dan 24 uur of
    /// buiten een toegangsmoment worden overgeslagen.
    /// </summary>
    public async Task<OfflineSyncResult> SyncOfflineAsync(Guid operatorId, string? installationId, IReadOnlyList<OfflineScan> scans, CancellationToken cancellationToken)
    {
        var device = await OperatorDeviceAsync(operatorId, installationId, cancellationToken);
        int accepted = 0, duplicates = 0, skipped = 0, conflicts = 0;
        foreach (var item in scans.OrderBy(s => s.ScannedAt))
        {
            if (await db.AccessScans.AnyAsync(s => s.ClientScanId == item.ClientScanId, cancellationToken))
            {
                duplicates++;
                continue;
            }

            // Klok van het toestel: niet in de toekomst en niet ouder dan een dag.
            var at = DateTime.SpecifyKind(item.ScannedAt, DateTimeKind.Utc) > Now ? Now : DateTime.SpecifyKind(item.ScannedAt, DateTimeKind.Utc);
            var access = at < Now.AddHours(-24) ? null : await windows.CurrentAsync(cancellationToken, new DateTimeOffset(at, TimeSpan.Zero));
            if (access is null)
            {
                skipped++;
                continue;
            }

            var (scan, _, _, _, _) = await EvaluateAsync(operatorId, device, item.Code, access, at, details: false, cancellationToken, fromQueue: true);
            scan.Offline = true;
            scan.ClientScanId = item.ClientScanId;
            scan.OfflineOutcome = item.LocalOutcome;
            scan.SyncedAt = Now;
            var admittedLocally = item.LocalOutcome is AccessOutcome.Admitted or AccessOutcome.AdmittedAgain or AccessOutcome.Warning;
            if (scan.Outcome == AccessOutcome.Warning && admittedLocally)
            {
                // Offline al binnengelaten: dat is de beslissing.
                scan.Decision = AccessDecision.Admitted;
                scan.DecidedAt = at;
            }

            if (admittedLocally && scan.Outcome == AccessOutcome.Refused)
            {
                conflicts++;
            }

            db.AccessScans.Add(scan);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                accepted++;
                await ReconcileLaterAsync(scan, access, cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Tegelijk door een tweede verzending opgeslagen (unieke client-id).
                db.ChangeTracker.Clear();
                duplicates++;
            }
        }

        return new OfflineSyncResult(accepted, duplicates, skipped, conflicts);
    }

    /// <summary>
    /// Een offline scan kan later binnenkomen dan een scan die erna gedaan werd (omgekeerde syncvolgorde). Was die latere
    /// scan "eerste keer" op een ander toestel, dan wordt hij "al eerder binnen via een ander toestel" (toegelaten), zodat
    /// het resultaat niet van de volgorde van synchroniseren afhangt.
    /// </summary>
    private async Task ReconcileLaterAsync(AccessScan earlier, AccessEvent access, CancellationToken cancellationToken)
    {
        if (!earlier.Admits || earlier.MemberId is null)
        {
            return;
        }

        var later = await (access.EventId is { } id
                ? db.AccessScans.Where(s => s.EventId == id)
                : db.AccessScans.Where(s => s.EventId == null && s.CarnivalDay == access.CarnivalDay))
            .Where(s => s.MemberId == earlier.MemberId && s.ScannedAt > earlier.ScannedAt && s.Outcome == AccessOutcome.Admitted
                && (s.Method == AccessMethod.Manual || s.OperatorDeviceId != earlier.OperatorDeviceId))
            .ToListAsync(cancellationToken);
        foreach (var scan in later)
        {
            scan.Outcome = AccessOutcome.Warning;
            scan.Reason = "OtherDevice";
            scan.Decision = AccessDecision.Admitted;
            scan.DecidedAt ??= scan.ScannedAt;
        }

        if (later.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>"Toch toelaten" of "Weigeren" bij oranje; alleen door wie scande en maar één keer.</summary>
    public async Task<AccessCounts> DecideAsync(Guid operatorId, Guid scanId, bool admit, CancellationToken cancellationToken)
    {
        var scan = await db.AccessScans.SingleOrDefaultAsync(s => s.Id == scanId && s.OperatorUserId == operatorId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Scan niet gevonden.", DomainErrorKind.NotFound);
        if (scan.Outcome != AccessOutcome.Warning || scan.Decision is not null)
        {
            throw new DomainException(ErrorCodes.InvalidStatusTransition, "Over deze scan is al beslist.", DomainErrorKind.Conflict);
        }

        scan.Decision = admit ? AccessDecision.Admitted : AccessDecision.Refused;
        scan.DecidedAt = Now;
        await db.SaveChangesAsync(cancellationToken);
        var access = scan.EventId is { } eventId
            ? AccessWindows.ForEvent(await db.Events.AsNoTracking().SingleAsync(e => e.Id == eventId, cancellationToken))
            : AccessWindows.ForCarnivalDay(scan.CarnivalDay!.Value);
        return await CountsAsync(access, cancellationToken);
    }

    /// <summary>Toegangskaart bij een lid in het portal: actieve activiteit, al binnen en de toegangshistorie van dit carnavalsjaar.</summary>
    public async Task<MemberAccess> MemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var current = await windows.CurrentAsync(cancellationToken);
        var inside = current is null ? null : await LastAdmissionAsync(current, memberId, cancellationToken);
        var problem = await TicketProblemAsync(memberId, cancellationToken);
        var rows = await (
            from s in db.AccessScans.AsNoTracking()
            where s.MemberId == memberId
            join e in db.Events.AsNoTracking() on s.EventId equals e.Id into es
            from e in es.DefaultIfEmpty()
            join u in db.Users.AsNoTracking() on s.OperatorUserId equals u.Id
            orderby s.ScannedAt descending
            select new { s.ScannedAt, s.Method, s.Outcome, s.Decision, EventTitle = e == null ? null : e.Title, s.CarnivalDay, u.DisplayName })
            .Take(20).ToListAsync(cancellationToken);
        var history = rows.Select(r => new AccessHistoryItem(r.ScannedAt, r.Method, r.Outcome, r.Decision,
            r.EventTitle ?? AccessWindows.ForCarnivalDay(r.CarnivalDay!.Value).Title, r.DisplayName)).ToList();
        return new MemberAccess(current, inside is not null, inside?.ScannedAt, problem, history);
    }

    /// <summary>Waarom een lid geen toegang heeft (niet actief, ticket geblokkeerd); <c>null</c> als het in orde is.</summary>
    private async Task<string?> TicketProblemAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
        if (member.EffectiveStatus != MembershipStatus.Active)
        {
            return "Geen actief lidmaatschap.";
        }

        var year = await db.CarnivalYears.AsNoTracking().Where(y => y.Active).Select(y => (int?)y.Id).SingleOrDefaultAsync(cancellationToken);
        var blocked = year is not null && await db.Tickets.AnyAsync(t => t.CarnivalYearId == year && t.MemberId == memberId && t.Status == TicketStatus.Blocked, cancellationToken);
        return blocked ? "Ticket geblokkeerd." : null;
    }

    /// <summary>
    /// Handmatig inchecken in het portal (leden zonder smartphone, OQ-23). Is het lid al binnen, dan niets vastleggen
    /// tenzij <paramref name="force"/> ("Toch opnieuw inchecken").
    /// </summary>
    public async Task<AccessResult> CheckInAsync(Guid operatorId, Guid memberId, bool force, CancellationToken cancellationToken)
    {
        var accessEvent = await RequireCurrentAsync(cancellationToken);
        var member = await db.Members.AsNoTracking().Where(m => m.Id == memberId).Select(m => m.FullName).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
        var problem = await TicketProblemAsync(memberId, cancellationToken);
        if (problem is not null)
        {
            throw new DomainException(ErrorCodes.TicketUnavailable, $"Geen toegang: {problem}", DomainErrorKind.Conflict);
        }

        var previous = await LastAdmissionAsync(accessEvent, memberId, cancellationToken);
        if (previous is not null && !force)
        {
            return new AccessResult(null, AccessOutcome.Warning, "Al binnen", $"{member} is vanavond al binnen sinds {Time(previous.ScannedAt)}.",
                member, previous.ScannedAt, true, await CountsAsync(accessEvent, cancellationToken));
        }

        var year = await db.CarnivalYears.AsNoTracking().Where(y => y.Active).Select(y => (int?)y.Id).SingleOrDefaultAsync(cancellationToken);
        var ticketId = year is null ? null : await db.Tickets.Where(t => t.CarnivalYearId == year && t.MemberId == memberId).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(cancellationToken);
        var scan = NewScan(accessEvent);
        scan.TicketId = ticketId;
        scan.MemberId = memberId;
        scan.Method = AccessMethod.Manual;
        scan.Outcome = previous is null ? AccessOutcome.Admitted : AccessOutcome.AdmittedAgain;
        scan.OperatorUserId = operatorId;
        scan.ScannedAt = Now;
        db.AccessScans.Add(scan);
        await db.SaveChangesAsync(cancellationToken);
        return new AccessResult(scan.Id, scan.Outcome, "Ingecheckt", $"{member} is ingecheckt om {Time(scan.ScannedAt)}.", member, previous?.ScannedAt, false,
            await CountsAsync(accessEvent, cancellationToken));
    }
}
