using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

/// <summary>Wie dit jaar de rol heeft (portal).</summary>
public sealed record RoyalHolder(string RoleCode, Guid UserId, string Name, DateOnly? ValidTo);

public sealed record RoyalInfoText(string RoleCode, string Body);

public sealed record RoyalOverview(IReadOnlyList<RoyalHolder> Holders, IReadOnlyList<RoyalInfoText> Infos);

/// <summary>Begroeting en informatie in de app voor de prins(es) of een adjudant.</summary>
public sealed record RoyalGreeting(string RoleCode, string Greeting, string? InfoHtml);

/// <summary>
/// Prins(es) en adjudanten (2026-10-10). De rollen ken je toe bij Gebruikers (maximaal één prins(es), twee adjudanten,
/// met een einddatum); de app begroet ze bovenaan de eerste pagina met een knop naar hun eigen informatie, die het bestuur
/// in het portal onder "Prins" beheert. Prinses of prins volgt het geslacht van het lid.
/// </summary>
public sealed class RoyalHousehold(DrammersDbContext db, IAuditLogger audit, IClock clock)
{
    public static readonly string[] RoleCodes = [DefaultRoles.Prins, DefaultRoles.Adjudant];

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    public async Task<RoyalOverview> OverviewAsync(CancellationToken cancellationToken)
    {
        var holders = await HoldersAsync(cancellationToken);
        var texts = await db.RoleInfos.AsNoTracking().Where(i => RoleCodes.Contains(i.RoleCode)).ToDictionaryAsync(i => i.RoleCode, i => i.Body, cancellationToken);
        return new RoyalOverview(
            [.. holders.Select(h => new RoyalHolder(h.RoleCode, h.UserId, h.Name, h.ValidTo))],
            [.. RoleCodes.Select(c => new RoyalInfoText(c, texts.GetValueOrDefault(c) ?? string.Empty))]);
    }

    public async Task SetInfoAsync(string roleCode, string? body, CancellationToken cancellationToken)
    {
        if (!RoleCodes.Contains(roleCode))
        {
            throw new DomainException(ErrorCodes.NotFound, "Onbekende rol.", DomainErrorKind.NotFound);
        }

        var text = body?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() ?? string.Empty;
        if (text.Length > 20000)
        {
            throw new DomainException(ErrorCodes.Validation, "De tekst mag hooguit 20.000 tekens zijn.");
        }

        var info = await db.RoleInfos.SingleOrDefaultAsync(i => i.RoleCode == roleCode, cancellationToken);
        var before = info?.Body;
        if (info is null)
        {
            db.RoleInfos.Add(new RoleInfo { RoleCode = roleCode, Body = text });
        }
        else
        {
            info.Body = text;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("role-info.changed", "RoleInfo", roleCode, before, text), cancellationToken);
    }

    /// <summary>De begroeting voor deze gebruiker, of <c>null</c> zonder geldige rol prins(es) of adjudant.</summary>
    public async Task<RoyalGreeting?> ForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var holders = await HoldersAsync(cancellationToken);
        var mine = holders.Where(h => h.UserId == userId).OrderBy(h => h.RoleCode == DefaultRoles.Prins ? 0 : 1).FirstOrDefault();
        if (mine is null)
        {
            return null;
        }

        var prince = holders.FirstOrDefault(h => h.RoleCode == DefaultRoles.Prins);
        var greeting = mine.RoleCode == DefaultRoles.Prins
            ? $"Welkom {Title(mine)} {mine.FirstName}"
            : prince is null ? $"Welkom adjudant {mine.FirstName}" : $"Welkom adjudant {mine.FirstName} van {Title(prince)} {prince.FirstName}";
        var body = await db.RoleInfos.AsNoTracking().Where(i => i.RoleCode == mine.RoleCode).Select(i => i.Body).SingleOrDefaultAsync(cancellationToken);
        return new RoyalGreeting(mine.RoleCode, greeting, MarkdownRenderer.ToSafeHtml(body));
    }

    private static string Title(Holder h) => h.Gender == "v" ? "prinses" : "prins";

    private sealed record Holder(string RoleCode, Guid UserId, string Name, string FirstName, string? Gender, DateOnly? ValidTo);

    /// <summary>Gebruikers met een vandaag geldige rol prins(es) of adjudant (actief account).</summary>
    private async Task<List<Holder>> HoldersAsync(CancellationToken cancellationToken)
    {
        var today = Today;
        var rows = await (
            from ur in db.UserRoles.AsNoTracking()
            join r in db.Roles on ur.RoleId equals r.Id
            join u in db.Users on ur.UserId equals u.Id
            where RoleCodes.Contains(r.Code) && u.AccountStatus == AccountStatus.Active
                && (ur.ValidFrom == null || ur.ValidFrom <= today) && (ur.ValidTo == null || ur.ValidTo >= today)
            join m in db.Members on u.MemberId equals m.Id into members
            from m in members.DefaultIfEmpty()
            orderby r.SortOrder, u.DisplayName
            select new { r.Code, u.Id, u.DisplayName, FullName = m == null ? null : m.FullName, FirstName = m == null ? null : m.FirstName, Gender = m == null ? null : m.Gender, ur.ValidTo })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(x => new Holder(x.Code, x.Id, x.FullName ?? x.DisplayName,
            x.FirstName ?? (x.FullName ?? x.DisplayName).Split(' ')[0], x.Gender, x.ValidTo))];
    }
}
