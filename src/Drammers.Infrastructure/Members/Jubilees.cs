using System.Globalization;
using System.Text.Json;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.CarnivalYears;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

public sealed record Jubilarian(
    Guid MemberId, string MemberNumber, string FullName, short? JoinYear, short? JoinYearOverride, short BaseYear, int Years, string? Note,
    bool HasEmail = false, DateTime? InvitedAt = null);

public sealed record MemberWithoutJoinYear(Guid MemberId, string MemberNumber, string FullName, string? City);

public sealed record JubileeReport(
    int CarnivalYearId, string CarnivalYearName, int ReferenceYear, IReadOnlyList<int> Milestones,
    IReadOnlyList<Jubilarian> Jubilarians, IReadOnlyList<MemberWithoutJoinYear> WithoutJoinYear, IReadOnlyList<JubileeYearOption> CarnivalYears);

/// <summary>Keuzelijst van carnavalsjaren; ook voor gebruikers zonder <c>config.manage</c>.</summary>
public sealed record JubileeYearOption(int Id, string Name, bool Active);

/// <summary>Aantal jaren lid in het actieve carnavalsjaar en, als dat een jubileum is, het jubileum.</summary>
public sealed record MemberJubilee(int YearsMember, bool IsJubilee);

/// <summary>
/// Jubilarissen (fase 20, OQ-30). Het jubileum telt in het kalenderjaar waarin carnaval valt: in carnavalsjaar 2025/2026
/// (carnaval februari 2026) zijn leden met inschrijfjaar 2015, 2004 en 1993 jubilaris (11, 22 en 33 jaar). Alleen actieve
/// leden tellen mee. Het bestuur kan per lid het jaar corrigeren waarvanaf het jubileum telt; de jubilea zelf staan in de
/// configuratie, zodat een andere regel geen deploy vraagt.
/// </summary>
public sealed class Jubilees(DrammersDbContext db, IAuditLogger audit, IClock clock)
{
    public static readonly IReadOnlyList<int> DefaultMilestones = [11, 22, 33, 44, 55, 66, 77];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Het jaar waarin carnaval van dit carnavalsjaar valt.</summary>
    public static int ReferenceYear(CarnivalYear year) => year.CarnivalStartDate.Year;

    public static int? YearsMember(short? baseYear, int referenceYear) =>
        baseYear is { } b && b <= referenceYear ? referenceYear - b : null;

    public async Task<IReadOnlyList<int>> GetMilestonesAsync(CancellationToken cancellationToken)
    {
        var value = await db.AppConfiguration.AsNoTracking()
            .Where(s => s.Key == AppConfigurationKeys.JubileeMilestones).Select(s => s.Value).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultMilestones;
        }

        var parsed = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(n => n > 0).Distinct().Order().ToList();
        return parsed.Count == 0 ? DefaultMilestones : parsed;
    }

    public async Task SetMilestonesAsync(IReadOnlyList<int> milestones, CancellationToken cancellationToken)
    {
        if (milestones.Count is 0 or > 20 || milestones.Any(m => m is < 1 or > 100))
        {
            throw new DomainException(ErrorCodes.Validation, "Geef 1 tot 20 jubilea op, elk tussen 1 en 100 jaar.");
        }

        var value = string.Join(',', milestones.Distinct().Order().Select(m => m.ToString(CultureInfo.InvariantCulture)));
        var setting = await db.AppConfiguration.SingleOrDefaultAsync(s => s.Key == AppConfigurationKeys.JubileeMilestones, cancellationToken);
        var before = setting?.Value;
        if (setting is null)
        {
            db.AppConfiguration.Add(new AppConfigurationSetting { Key = AppConfigurationKeys.JubileeMilestones, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("config.jubilee-milestones.changed", "AppConfiguration", AppConfigurationKeys.JubileeMilestones,
            before is null ? null : JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(value, Json)), cancellationToken);
    }

    /// <summary>Jubilarissen van een carnavalsjaar (zonder id: het actieve) en de actieve leden zonder inschrijfjaar.</summary>
    public async Task<JubileeReport> BuildAsync(int? carnivalYearId, CancellationToken cancellationToken)
    {
        var year = await (carnivalYearId is { } id
                ? db.CarnivalYears.AsNoTracking().SingleOrDefaultAsync(y => y.Id == id, cancellationToken)
                : db.CarnivalYears.AsNoTracking().SingleOrDefaultAsync(y => y.Active, cancellationToken))
            ?? throw new DomainException(ErrorCodes.NotFound, "Carnavalsjaar niet gevonden.", DomainErrorKind.NotFound);
        var reference = ReferenceYear(year);
        var milestones = await GetMilestonesAsync(cancellationToken);

        var active = await db.Members.AsNoTracking()
            .Where(m => (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active)
            // Gesplitste leden (2026-10-10): alleen het hoofdlid; het tweede lid heeft dezelfde jaren en staat er niet apart in.
            .Where(m => m.MembershipKind != MembershipKind.Partner)
            .Select(m => new
            {
                m.Id,
                m.MemberNumber,
                m.FullName,
                m.LastName,
                m.FirstName,
                m.City,
                m.JoinYear,
                m.JubileeJoinYearOverride,
                m.JubileeNote,
                HasEmail = m.Email != null && m.Email != string.Empty,
            })
            .ToListAsync(cancellationToken);
        var invited = await db.JubileeInvitations.AsNoTracking().Where(i => i.CarnivalYearId == year.Id)
            .ToDictionaryAsync(i => i.MemberId, i => i.InvitedAt, cancellationToken);

        var jubilarians = active
            .Select(m => new { Member = m, BaseYear = m.JubileeJoinYearOverride ?? m.JoinYear })
            .Select(x => new { x.Member, x.BaseYear, Years = YearsMember(x.BaseYear, reference) })
            .Where(x => x.Years is { } years && milestones.Contains(years))
            .OrderByDescending(x => x.Years).ThenBy(x => x.Member.LastName ?? x.Member.FullName).ThenBy(x => x.Member.FirstName)
            .Select(x => new Jubilarian(x.Member.Id, x.Member.MemberNumber, x.Member.FullName, x.Member.JoinYear,
                x.Member.JubileeJoinYearOverride, x.BaseYear!.Value, x.Years!.Value, x.Member.JubileeNote, x.Member.HasEmail,
                invited.TryGetValue(x.Member.Id, out var at) ? at : null))
            .ToList();

        var withoutJoinYear = active.Where(m => (m.JubileeJoinYearOverride ?? m.JoinYear) is null)
            .OrderBy(m => m.LastName ?? m.FullName).ThenBy(m => m.FirstName)
            .Select(m => new MemberWithoutJoinYear(m.Id, m.MemberNumber, m.FullName, m.City))
            .ToList();

        var years = await db.CarnivalYears.AsNoTracking().OrderByDescending(y => y.StartDate)
            .Select(y => new JubileeYearOption(y.Id, y.Name, y.Active)).ToListAsync(cancellationToken);
        return new JubileeReport(year.Id, year.Name, reference, milestones, jubilarians, withoutJoinYear, years);
    }

    /// <summary>Het jaar waarvanaf het jubileum telt aanpassen; leeg = het inschrijfjaar volgen. De sync raakt dit nooit aan.</summary>
    public async Task SetOverrideAsync(Guid memberId, short? joinYearOverride, string? note, CancellationToken cancellationToken)
    {
        var thisYear = clock.UtcNow.Year;
        if (joinYearOverride is { } y && (y < 1900 || y > thisYear + 1))
        {
            throw new DomainException(ErrorCodes.Validation, $"Kies een jaar tussen 1900 en {thisYear + 1}.");
        }

        var member = await db.Members.SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
        var before = JsonSerializer.Serialize(new { member.JubileeJoinYearOverride, member.JubileeNote }, Json);
        member.JubileeJoinYearOverride = joinYearOverride;
        member.JubileeNote = joinYearOverride is null ? null : string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.jubilee-year.changed", "Member", memberId.ToString(), before,
            JsonSerializer.Serialize(new { member.JubileeJoinYearOverride, member.JubileeNote }, Json)), cancellationToken);
    }

    /// <summary>Voor "Mijn gegevens" in de app; null zonder inschrijfjaar of zonder actief carnavalsjaar.</summary>
    public async Task<MemberJubilee?> ForMemberAsync(Member member, CancellationToken cancellationToken)
    {
        var year = await db.CarnivalYears.AsNoTracking().SingleOrDefaultAsync(y => y.Active, cancellationToken);
        if (year is null || YearsMember(member.JubileeBaseYear, ReferenceYear(year)) is not { } years)
        {
            return null;
        }

        var milestones = await GetMilestonesAsync(cancellationToken);
        return new MemberJubilee(years, member.EffectiveStatus == MembershipStatus.Active && milestones.Contains(years));
    }
}
