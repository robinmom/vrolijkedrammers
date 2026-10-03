using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;
using Drammers.Api.Authorization;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Ledenlijst, lid-detail, lokale velden, export en (alleen Dev/Acc) alles verwijderen (fase 8, docs/05 §6).</summary>
[ApiController]
[Route("api/v1/admin/members")]
public sealed class AdminMembersController(
    DrammersDbContext db, MemberAdministration members, MemberSyncSettings settings, IAuditLogger audit) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<PagedResult<MemberSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<MemberSummaryResponse>> Search(
        [FromQuery] string? search, [FromQuery] MembershipStatus? status, [FromQuery] MemberSyncState? syncState,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<MemberSummaryResponse>.Normalize(page, pageSize);
        var query = Filter(search, status, syncState);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(m => m.LastName).ThenBy(m => m.FirstName).ThenBy(m => m.MemberNumber)
            .Skip((p - 1) * size).Take(size)
            .Select(m => new MemberSummaryResponse(m.Id, m.MemberNumber, m.FullName, m.Email, m.City,
                m.LocalStatusOverride ?? m.MembershipStatus, m.SyncState, m.JoinYear,
                db.Users.Any(u => u.MemberId == m.Id && u.AccountStatus == AccountStatus.Active)))
            .ToListAsync(cancellationToken);
        return new PagedResult<MemberSummaryResponse>(items, p, size, total);
    }

    /// <summary>Kengetallen voor de ledenpagina: aantallen per status, ontbrekend in e-Boekhouden, met app-account.</summary>
    [HttpGet("summary")]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<MemberSummaryCountsResponse>(StatusCodes.Status200OK)]
    public async Task<MemberSummaryCountsResponse> Summary(CancellationToken cancellationToken)
    {
        var counts = await db.Members.AsNoTracking()
            .GroupBy(m => m.LocalStatusOverride ?? m.MembershipStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        int Count(MembershipStatus status) => counts.SingleOrDefault(c => c.Status == status)?.Count ?? 0;
        var missing = await db.Members.CountAsync(m => m.SyncState == MemberSyncState.Missing, cancellationToken);
        var withAccount = await db.Members.CountAsync(
            m => (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active
                && db.Users.Any(u => u.MemberId == m.Id && u.AccountStatus == AccountStatus.Active), cancellationToken);
        var lastSync = await db.SyncJobs.AsNoTracking()
            .Where(j => !j.DryRun && (j.Status == Modules.Import.Sync.SyncJobStatus.Succeeded
                || j.Status == Modules.Import.Sync.SyncJobStatus.SucceededWithWarnings || j.Status == Modules.Import.Sync.SyncJobStatus.Conflict))
            .OrderByDescending(j => j.CompletedAt).Select(j => j.CompletedAt).FirstOrDefaultAsync(cancellationToken);
        return new MemberSummaryCountsResponse(
            Count(MembershipStatus.Active), Count(MembershipStatus.Inactive), Count(MembershipStatus.Suspended), Count(MembershipStatus.Deceased),
            missing, withAccount, lastSync);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<MemberDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<MemberDetailResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var m = await db.Members.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
        var account = await db.Users.AsNoTracking().Where(u => u.MemberId == id)
            .Select(u => new MemberAccountResponse(u.Id, u.Email, u.AccountStatus.ToString(), u.LastLoginAt, u.ExternalObjectId.StartsWith(PendingObjectId.Prefix)))
            .SingleOrDefaultAsync(cancellationToken);
        var mapping = await settings.GetMappingAsync(cancellationToken);
        var groups = await db.GroupMemberships.AsNoTracking().Where(gm => gm.MemberId == id)
            .Join(db.Groups, gm => gm.GroupId, g => g.Id, (gm, g) => new { g.Id, g.Name, gm.Function, gm.ValidTo })
            .OrderBy(g => g.Name)
            .Select(g => new MemberGroupResponse(g.Id, g.Name, g.Function, g.ValidTo))
            .ToListAsync(cancellationToken);
        var provisioning = await db.AccountProvisioning.AsNoTracking()
            .Where(p => p.MemberId == id && p.Kind == Modules.Identity.Provisioning.ProvisioningKind.Member)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new MemberProvisioningResponse(p.Id, p.Step, p.Attempts, p.LastError, p.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);
        var kader = await db.CommitteeMembers.AsNoTracking().Where(k => k.MemberId == id)
            .Join(db.Committees, k => k.CommitteeId, c => c.Id, (k, c) => new MemberKaderResponse(k.Id, c.Id, c.Name, k.Function))
            .ToListAsync(cancellationToken);
        return new MemberDetailResponse(
            m.Id, m.MemberNumber, m.EbMemberId, m.FullName, m.FirstName, m.NamePrefix, m.LastName, m.NameCorrectedManually,
            m.Salutation, m.Gender, m.AddressLine, m.PostalCode, m.City, m.Country, m.Email, m.Phone, m.MobilePhone,
            m.BirthDate, m.JoinYear, m.EbStatusRaw, m.MemberCategory, m.ParadeGroupName, MembershipWeights.Persons(m.EbStatusRaw),
            m.MembershipStatus, m.LocalStatusOverride, m.LocalStatusOverride ?? m.MembershipStatus, m.MembershipValidFrom, m.MembershipValidTo,
            m.SyncState, m.EbLastSeenAt, m.EbMissingSince,
            new MemberFieldSourcesResponse(mapping.BirthDate is not null, mapping.JoinYear is not null, mapping.Status is not null, mapping.Category is not null),
            account, groups, provisioning, kader, m.JubileeJoinYearOverride, m.JubileeNote,
            m.LocalFields is { Length: > 0 } local ? local.Split(',') : []);
    }

    [HttpPatch("{id:guid}")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid id, MemberLocalUpdateRequest request, CancellationToken cancellationToken)
    {
        await members.UpdateLocalAsync(id, new MemberLocalUpdate(request.LocalStatusOverride, request.MembershipValidFrom, request.MembershipValidTo,
            request.FirstName, request.NamePrefix, request.LastName, request.BirthDate, request.JoinYear), cancellationToken);
        return NoContent();
    }

    /// <summary>Alle gegevens bewerken, ook die uit e-Boekhouden (fase 24); gewijzigde velden overschrijft de sync niet meer.</summary>
    [HttpPut("{id:guid}/data")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateData(Guid id, MemberDataRequest request, CancellationToken cancellationToken)
    {
        await members.UpdateDataAsync(id, new MemberDataUpdate(request.FullName, request.Salutation, request.Gender, request.AddressLine,
            request.PostalCode, request.City, request.Country, request.Email, request.Phone, request.MobilePhone, request.BirthDate,
            request.JoinYear, request.MemberCategory, request.ParadeGroupName), cancellationToken);
        return NoContent();
    }

    /// <summary>Handmatig aangepaste velden teruggeven aan e-Boekhouden; de volgende sync neemt ze weer over.</summary>
    [HttpDelete("{id:guid}/local-fields")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ReleaseLocalFields(Guid id, CancellationToken cancellationToken)
    {
        await members.ReleaseLocalFieldsAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/confirm-inactive")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmInactive(Guid id, CancellationToken cancellationToken)
    {
        await members.ConfirmInactiveAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Ledenlijst als Excel; de export wordt geaudit (aantal en filter, geen namen).</summary>
    [HttpGet("export")]
    [RequirePermission(Permissions.MemberExport)]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> Export(
        [FromQuery] string? search, [FromQuery] MembershipStatus? status, [FromQuery] MemberSyncState? syncState, CancellationToken cancellationToken)
    {
        var rows = await Filter(search, status, syncState).OrderBy(m => m.LastName).ThenBy(m => m.FirstName).ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Leden");
        string[] headers =
        [
            "Lidnummer", "Naam", "Voornaam", "Tussenvoegsel", "Achternaam", "Adres", "Postcode", "Plaats", "E-mailadres",
            "Telefoon", "Mobiel", "Geboortedatum", "Inschrijfjaar", "Categorie", "Status", "Sync",
        ];
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var m = rows[i];
            object?[] values =
            [
                m.MemberNumber, m.FullName, m.FirstName, m.NamePrefix, m.LastName, m.AddressLine, m.PostalCode, m.City, m.Email,
                m.Phone, m.MobilePhone, m.BirthDate?.ToDateTime(TimeOnly.MinValue), m.JoinYear, m.MemberCategory,
                StatusLabel(m.LocalStatusOverride ?? m.MembershipStatus), m.SyncState.ToString(),
            ];
            for (var c = 0; c < values.Length; c++)
            {
                sheet.Cell(i + 2, c + 1).Value = XLCellValue.FromObject(values[c]);
            }
        }

        sheet.Column(12).Style.DateFormat.Format = "dd-mm-yyyy";
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        await audit.WriteAsync(new AuditEntry("member.exported", "Member", "*", null, System.Text.Json.JsonSerializer.Serialize(new
        {
            count = rows.Count,
            search,
            status = status?.ToString(),
            syncState = syncState?.ToString(),
        })), cancellationToken);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"leden-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Alle leden en syncgegevens verwijderen en de nachtelijke sync uitzetten. Alleen in Dev en Acc; vraagt de tekst
    /// "LEDEN VERWIJDEREN" ter bevestiging.
    /// </summary>
    [HttpPost("purge")]
    [RequirePermission(Permissions.MemberPurge)]
    [ProducesResponseType<MemberPurgeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<MemberPurgeResponse> Purge(MemberPurgeRequest request, CancellationToken cancellationToken)
    {
        var result = await members.PurgeAsync(request.Confirmation, cancellationToken);
        return new MemberPurgeResponse(result.Members, result.SyncJobs, result.UnlinkedAccounts);
    }

    /// <summary>
    /// Eén lid volledig uit de app verwijderen (recht <c>member.privacy</c>, typ "VERWIJDEREN"): accounts, gegevens en het lid
    /// zelf; het lidnummer wordt uitgesloten van de sync. e-Boekhouden blijft ongemoeid.
    /// </summary>
    [HttpPost("{id:guid}/remove")]
    [RequirePermission(Permissions.MemberPrivacy)]
    [ProducesResponseType<MemberRemovalResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<MemberRemovalResponse> Remove(Guid id, MemberRemovalRequest request, CancellationToken cancellationToken)
    {
        var result = await members.RemoveCompletelyAsync(id, request.Confirmation, cancellationToken);
        return new MemberRemovalResponse(result.MemberNumber, result.Accounts, result.Applications);
    }

    /// <summary>Lidnummers die de sync overslaat (volledig uit de app verwijderd, nog in e-Boekhouden).</summary>
    [HttpGet("excluded")]
    [RequirePermission(Permissions.MemberPrivacy)]
    [ProducesResponseType<IReadOnlyList<ExcludedMemberResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ExcludedMemberResponse>> Excluded(CancellationToken cancellationToken) =>
        await db.ExcludedMembers.AsNoTracking().OrderByDescending(e => e.ExcludedAt)
            .GroupJoin(db.Users, e => e.ExcludedBy, u => u.Id, (e, us) => new { e, us })
            .SelectMany(x => x.us.DefaultIfEmpty(), (x, u) => new ExcludedMemberResponse(x.e.MemberNumber, x.e.ExcludedAt, u == null ? null : u.DisplayName))
            .ToListAsync(cancellationToken);

    [HttpDelete("excluded/{memberNumber}")]
    [RequirePermission(Permissions.MemberPrivacy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IncludeAgain(string memberNumber, CancellationToken cancellationToken)
    {
        await members.IncludeAgainAsync(memberNumber, cancellationToken);
        return NoContent();
    }

    private IQueryable<Member> Filter(string? search, MembershipStatus? status, MemberSyncState? syncState)
    {
        var query = db.Members.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m => m.FullName.Contains(term) || m.MemberNumber == term || (m.Email != null && m.Email.Contains(term)) || (m.City != null && m.City.Contains(term)));
        }

        if (status is { } s)
        {
            query = query.Where(m => (m.LocalStatusOverride ?? m.MembershipStatus) == s);
        }

        if (syncState is { } state)
        {
            query = query.Where(m => m.SyncState == state);
        }

        return query;
    }

    private static string StatusLabel(MembershipStatus status) => status switch
    {
        MembershipStatus.Active => "Actief",
        MembershipStatus.Inactive => "Inactief",
        MembershipStatus.Suspended => "Geschorst",
        MembershipStatus.Deceased => "Overleden",
        _ => status.ToString(),
    };
}

public sealed record MemberSummaryResponse(
    Guid Id, string MemberNumber, string FullName, string? Email, string? City, MembershipStatus Status, MemberSyncState SyncState,
    short? JoinYear, bool HasAccount);

public sealed record MemberFieldSourcesResponse(bool BirthDateFromEBoekhouden, bool JoinYearFromEBoekhouden, bool StatusFromEBoekhouden, bool CategoryFromEBoekhouden);

/// <summary>App-account van een lid; <c>AwaitingFirstSignIn</c>: goedgekeurd, maar nog geen eigen inlog gemaakt (ADR-014, herzien 2026-09-27).</summary>
public sealed record MemberAccountResponse(Guid UserId, string Email, string AccountStatus, DateTime? LastLoginAt, bool AwaitingFirstSignIn);

public sealed record MemberDetailResponse(
    Guid Id, string MemberNumber, int? EbMemberId, string FullName, string? FirstName, string? NamePrefix, string? LastName,
    bool NameCorrectedManually, string? Salutation, string? Gender, string? AddressLine, string? PostalCode, string? City,
    string? Country, string? Email, string? Phone, string? MobilePhone, DateOnly? BirthDate, short? JoinYear, string? EbStatusRaw,
    string? MemberCategory, string? ParadeGroupName, int Persons, MembershipStatus SyncedStatus, MembershipStatus? LocalStatusOverride, MembershipStatus EffectiveStatus,
    DateOnly? MembershipValidFrom, DateOnly? MembershipValidTo, MemberSyncState SyncState, DateTime? EbLastSeenAt,
    DateTime? EbMissingSince, MemberFieldSourcesResponse FieldSources, MemberAccountResponse? Account, IReadOnlyList<MemberGroupResponse> Groups,
    MemberProvisioningResponse? Provisioning, IReadOnlyList<MemberKaderResponse>? Kader = null, short? JubileeJoinYearOverride = null,
    string? JubileeNote = null, IReadOnlyList<string>? LocalFields = null);

/// <summary>Alle velden die anders uit e-Boekhouden komen; zie <see cref="MemberDataUpdate"/>.</summary>
public sealed record MemberDataRequest(
    [param: Required, StringLength(100)] string FullName,
    [param: StringLength(50)] string? Salutation,
    [param: StringLength(1)] string? Gender,
    [param: StringLength(150)] string? AddressLine,
    [param: StringLength(50)] string? PostalCode,
    [param: StringLength(50)] string? City,
    [param: StringLength(50)] string? Country,
    [param: StringLength(150)] string? Email,
    [param: StringLength(50)] string? Phone,
    [param: StringLength(50)] string? MobilePhone,
    DateOnly? BirthDate,
    short? JoinYear,
    [param: StringLength(50)] string? MemberCategory,
    [param: StringLength(100)] string? ParadeGroupName);

/// <summary>Functie in het kader op de website (fase 21a); beheer onder Website → Kader.</summary>
public sealed record MemberKaderResponse(Guid Id, int CommitteeId, string Committee, string? Function);

/// <summary>Laatste provisioning van een account voor dit lid (fase 9).</summary>
public sealed record MemberProvisioningResponse(
    Guid Id, Modules.Identity.Provisioning.ProvisioningStep Step, int Attempts, string? LastError, DateTime CreatedAt);

public sealed record MemberGroupResponse(Guid GroupId, string Name, Modules.Membership.Groups.GroupFunction Function, DateOnly? ValidTo);

public sealed record MemberSummaryCountsResponse(
    int Active, int Inactive, int Suspended, int Deceased, int MissingInEBoekhouden, int ActiveWithAccount, DateTime? LastSyncAt);

public sealed record MemberLocalUpdateRequest(
    MembershipStatus? LocalStatusOverride, DateOnly? MembershipValidFrom, DateOnly? MembershipValidTo, string? FirstName,
    string? NamePrefix, string? LastName, DateOnly? BirthDate, short? JoinYear);

public sealed record MemberPurgeRequest(string Confirmation);

public sealed record MemberRemovalRequest(string Confirmation);

public sealed record MemberRemovalResponse(string MemberNumber, int Accounts, int Applications);

public sealed record ExcludedMemberResponse(string MemberNumber, DateTime ExcludedAt, string? ExcludedBy);

public sealed record MemberPurgeResponse(int Members, int SyncJobs, int UnlinkedAccounts);
