using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Parade.Judging;
using Drammers.Modules.Parade.Parades;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.ParadeManagement;

public sealed record JurorView(Guid UserId, string Name, string Email, bool Invited, bool HeadJury, IReadOnlyList<int> CategoryIds);

public sealed record JudgingCategoryView(
    int CategoryId, string Name, bool Judged, int WeightOriginality, int WeightCarnivalesque, int WeightQuality, int WeightOverall,
    int JurorCount, int EntryCount);

public sealed record JuryOverview(Guid ParadeId, string ParadeName, DateOnly ParadeDate, IReadOnlyList<JurorView> Jurors, IReadOnlyList<JudgingCategoryView> Categories);

public sealed record JudgingWeights(bool Judged, int Originality, int Carnivalesque, int Quality, int Overall);

/// <summary>
/// De jury van een optocht (fase 22a). Juryleden zijn accounts met de rol Jury, zonder ledenbestand: het bestuur nodigt ze
/// uit met naam en e-mail; bij de eerste aanmelding met dat adres koppelt de app het account (ADR-014). Per optocht wijst
/// het bestuur of de hoofdjury ze toe aan categorieën; de weging per categorie stelt het bestuur in. Een nieuwe optocht
/// neemt weging en indeling over van de vorige.
/// </summary>
public sealed class ParadeJury(
    DrammersDbContext db,
    MemberAccounts accounts,
    AccountAdministration administration,
    IEmailSender email,
    IAuditLogger audit,
    IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DateTime Now => clock.UtcNow.UtcDateTime;

    /// <summary>De optocht met dit id, of de huidige als er geen id is.</summary>
    public async Task<Parade> ParadeAsync(Guid? paradeId, CancellationToken cancellationToken) =>
        await (paradeId is { } id ? db.Parades.Where(p => p.Id == id) : db.CurrentParades()).AsNoTracking().FirstOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Er is (nog) geen optocht.", DomainErrorKind.NotFound);

    public async Task<JuryOverview> OverviewAsync(Guid? paradeId, CancellationToken cancellationToken)
    {
        var parade = await ParadeAsync(paradeId, cancellationToken);
        await EnsureCategoriesAsync(parade.Id, cancellationToken);

        var juryRole = await RoleIdAsync(DefaultRoles.Jury, cancellationToken);
        var headRole = await RoleIdAsync(DefaultRoles.Hoofdjury, cancellationToken);
        var jurors = await db.Users.AsNoTracking()
            .Where(u => u.AccountStatus != AccountStatus.Deleted && u.Roles.Any(r => r.RoleId == juryRole))
            .OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.ExternalObjectId, Head = u.Roles.Any(r => r.RoleId == headRole) })
            .ToListAsync(cancellationToken);
        var assignments = await db.ParadeJurorAssignments.AsNoTracking().Where(a => a.ParadeId == parade.Id).ToListAsync(cancellationToken);

        var categories = await (
            from j in db.ParadeJudgingCategories.AsNoTracking().Where(j => j.ParadeId == parade.Id)
            join c in db.ParadeCategories.AsNoTracking() on j.CategoryId equals c.Id
            orderby c.SortOrder, c.Name
            select new { j, c.Name, c.Active }).ToListAsync(cancellationToken);
        var entries = await db.ParadeRegistrations.AsNoTracking()
            .Where(r => r.ParadeId == parade.Id && r.CategoryId != null && r.RegistrationNumber != null
                && r.Status != RegistrationStatus.Withdrawn && r.Status != RegistrationStatus.Rejected)
            .GroupBy(r => r.CategoryId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);

        return new JuryOverview(parade.Id, parade.Name, parade.ParadeDate,
            [.. jurors.Select(u => new JurorView(u.Id, u.DisplayName, u.Email, PendingObjectId.IsPending(u.ExternalObjectId), u.Head,
                [.. assignments.Where(a => a.UserId == u.Id).Select(a => a.CategoryId).Order()]))],
            [.. categories.Where(c => c.Active || assignments.Any(a => a.CategoryId == c.j.CategoryId)).Select(c => new JudgingCategoryView(
                c.j.CategoryId, c.Name, c.j.Judged, c.j.WeightOriginality, c.j.WeightCarnivalesque, c.j.WeightQuality, c.j.WeightOverall,
                assignments.Count(a => a.CategoryId == c.j.CategoryId), entries.SingleOrDefault(e => e.Key == c.j.CategoryId)?.Count ?? 0))]);
    }

    /// <summary>Nodigt een jurylid uit: account met de rol Jury (zonder lid) en een e-mail met uitleg over inloggen.</summary>
    public async Task<Guid> InviteAsync(string name, string emailAddress, CancellationToken cancellationToken)
    {
        var cleanName = name.Trim();
        var address = emailAddress.Trim().ToLowerInvariant();
        if (cleanName.Length is 0 or > 100 || address.Length is 0 or > 254 || !address.Contains('@', StringComparison.Ordinal))
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een naam (hooguit 100 tekens) en een geldig e-mailadres in.");
        }

        var userId = await accounts.EnsureAccountAsync(address, cleanName, null, DefaultRoles.Jury, cancellationToken);
        await audit.WriteAsync(new AuditEntry("jury.invited", "User", userId.ToString(), null, JsonSerializer.Serialize(new { name = cleanName, email = address }, Json)), cancellationToken);
        await email.SendAsync(InviteMail(address, cleanName), cancellationToken);
        return userId;
    }

    public async Task ResendInviteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await JurorAsync(userId, cancellationToken);
        await email.SendAsync(InviteMail(user.Email, user.DisplayName), cancellationToken);
        await audit.WriteAsync(new AuditEntry("jury.invite-resent", "User", userId.ToString()), cancellationToken);
    }

    /// <summary>Categorieën van een jurylid in deze optocht (bestuur of hoofdjury).</summary>
    public async Task SetCategoriesAsync(Guid paradeId, Guid userId, IReadOnlyCollection<int> categoryIds, CancellationToken cancellationToken)
    {
        await JurorAsync(userId, cancellationToken);
        await EnsureCategoriesAsync(paradeId, cancellationToken);
        var wanted = categoryIds.Distinct().ToHashSet();
        var known = await db.ParadeJudgingCategories.Where(j => j.ParadeId == paradeId && wanted.Contains(j.CategoryId)).Select(j => j.CategoryId).ToListAsync(cancellationToken);
        if (known.Count != wanted.Count)
        {
            throw new DomainException(ErrorCodes.Validation, "Onbekende categorie voor deze optocht.");
        }

        var current = await db.ParadeJurorAssignments.Where(a => a.ParadeId == paradeId && a.UserId == userId).ToListAsync(cancellationToken);
        var before = current.Select(a => a.CategoryId).Order().ToList();
        db.ParadeJurorAssignments.RemoveRange(current.Where(a => !wanted.Contains(a.CategoryId)));
        foreach (var id in wanted.Where(id => current.All(a => a.CategoryId != id)))
        {
            db.ParadeJurorAssignments.Add(new ParadeJurorAssignment { ParadeId = paradeId, UserId = userId, CategoryId = id, AssignedAt = Now });
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("jury.categories-changed", "User", userId.ToString(), JsonSerializer.Serialize(before, Json),
            JsonSerializer.Serialize(new { paradeId, categories = wanted.Order() }, Json)), cancellationToken);
    }

    /// <summary>Hoofdjury aan of uit (alleen het bestuur): de hoofdjury mag in het portal juryleden indelen.</summary>
    public async Task SetHeadJuryAsync(Guid userId, bool headJury, CancellationToken cancellationToken)
    {
        await JurorAsync(userId, cancellationToken);
        var roles = await db.UserRoles.AsNoTracking().Where(r => r.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new RoleAssignment(r.Code, ur.ValidFrom, ur.ValidTo))
            .ToListAsync(cancellationToken);
        var has = roles.Any(r => r.RoleCode == DefaultRoles.Hoofdjury);
        if (has == headJury)
        {
            return;
        }

        await administration.SetUserRolesAsync(userId,
            headJury ? [.. roles, new RoleAssignment(DefaultRoles.Hoofdjury)] : [.. roles.Where(r => r.RoleCode != DefaultRoles.Hoofdjury)],
            cancellationToken);
    }

    /// <summary>Uit de jury halen: de rollen Jury en Hoofdjury eraf en geen categorieën meer (het account blijft bestaan).</summary>
    public async Task RemoveAsync(Guid userId, CancellationToken cancellationToken)
    {
        await JurorAsync(userId, cancellationToken);
        var roles = await db.UserRoles.AsNoTracking().Where(r => r.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new RoleAssignment(r.Code, ur.ValidFrom, ur.ValidTo))
            .ToListAsync(cancellationToken);
        await db.ParadeJurorAssignments.Where(a => a.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await administration.SetUserRolesAsync(userId, [.. roles.Where(r => r.RoleCode is not (DefaultRoles.Jury or DefaultRoles.Hoofdjury))], cancellationToken);
        await audit.WriteAsync(new AuditEntry("jury.removed", "User", userId.ToString()), cancellationToken);
    }

    /// <summary>Weging en "wordt beoordeeld" van een categorie in deze optocht (alleen het bestuur).</summary>
    public async Task SetWeightsAsync(Guid paradeId, int categoryId, JudgingWeights weights, CancellationToken cancellationToken)
    {
        if (new[] { weights.Originality, weights.Carnivalesque, weights.Quality, weights.Overall }.Any(w => w is < 0 or > ParadeJudgingCategory.MaxWeight)
            || weights.Originality + weights.Carnivalesque + weights.Quality + weights.Overall == 0)
        {
            throw new DomainException(ErrorCodes.Validation, $"Een weging is 0 tot en met {ParadeJudgingCategory.MaxWeight}, en minstens één criterium telt mee.");
        }

        await EnsureCategoriesAsync(paradeId, cancellationToken);
        var row = await db.ParadeJudgingCategories.SingleOrDefaultAsync(j => j.ParadeId == paradeId && j.CategoryId == categoryId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.Validation, "Onbekende categorie voor deze optocht.", DomainErrorKind.NotFound);
        var before = JsonSerializer.Serialize(new JudgingWeights(row.Judged, row.WeightOriginality, row.WeightCarnivalesque, row.WeightQuality, row.WeightOverall), Json);
        (row.Judged, row.WeightOriginality, row.WeightCarnivalesque, row.WeightQuality, row.WeightOverall) =
            (weights.Judged, weights.Originality, weights.Carnivalesque, weights.Quality, weights.Overall);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("jury.weights-changed", "ParadeJudgingCategory", $"{paradeId}/{categoryId}", before, JsonSerializer.Serialize(weights, Json)), cancellationToken);
    }

    /// <summary>
    /// Zorgt dat elke categorie van de optocht jury-instellingen heeft: overgenomen van de vorige optocht (weging en
    /// indeling), anders de standaard. Wordt ook aangeroepen bij het aanmaken van een optocht.
    /// </summary>
    public async Task EnsureCategoriesAsync(Guid paradeId, CancellationToken cancellationToken)
    {
        var existing = await db.ParadeJudgingCategories.Where(j => j.ParadeId == paradeId).Select(j => j.CategoryId).ToListAsync(cancellationToken);
        var categories = await db.ParadeCategories.AsNoTracking().Where(c => c.Active && (c.ParadeId == null || c.ParadeId == paradeId)).ToListAsync(cancellationToken);
        var missing = categories.Where(c => !existing.Contains(c.Id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var parade = await db.Parades.AsNoTracking().SingleAsync(p => p.Id == paradeId, cancellationToken);
        var previous = await db.Parades.AsNoTracking()
            .Where(p => p.Id != paradeId && (p.ParadeDate < parade.ParadeDate || (p.ParadeDate == parade.ParadeDate && p.CreatedAt < parade.CreatedAt)))
            .OrderByDescending(p => p.ParadeDate).ThenByDescending(p => p.CreatedAt).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        var template = previous is { } prev
            ? await db.ParadeJudgingCategories.AsNoTracking().Where(j => j.ParadeId == prev).ToListAsync(cancellationToken)
            : [];
        var juryRole = await RoleIdAsync(DefaultRoles.Jury, cancellationToken);
        var templateJurors = previous is { } p2
            ? await db.ParadeJurorAssignments.AsNoTracking()
                .Where(a => a.ParadeId == p2 && db.UserRoles.Any(r => r.UserId == a.UserId && r.RoleId == juryRole))
                .ToListAsync(cancellationToken)
            : [];

        foreach (var category in missing)
        {
            var copy = template.SingleOrDefault(t => t.CategoryId == category.Id);
            db.ParadeJudgingCategories.Add(copy is null
                ? ParadeJudgingCategory.Default(paradeId, category)
                : new ParadeJudgingCategory
                {
                    ParadeId = paradeId,
                    CategoryId = category.Id,
                    Judged = copy.Judged,
                    WeightOriginality = copy.WeightOriginality,
                    WeightCarnivalesque = copy.WeightCarnivalesque,
                    WeightQuality = copy.WeightQuality,
                    WeightOverall = copy.WeightOverall,
                });
            foreach (var a in templateJurors.Where(a => a.CategoryId == category.Id))
            {
                db.ParadeJurorAssignments.Add(new ParadeJurorAssignment { ParadeId = paradeId, UserId = a.UserId, CategoryId = category.Id, AssignedAt = Now });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<User> JurorAsync(Guid userId, CancellationToken cancellationToken)
    {
        var juryRole = await RoleIdAsync(DefaultRoles.Jury, cancellationToken);
        return await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId && u.AccountStatus != AccountStatus.Deleted && u.Roles.Any(r => r.RoleId == juryRole), cancellationToken)
            ?? throw new DomainException(ErrorCodes.UserNotFound, "Jurylid niet gevonden.", DomainErrorKind.NotFound);
    }

    private async Task<int> RoleIdAsync(string code, CancellationToken cancellationToken) =>
        await db.Roles.Where(r => r.Code == code).Select(r => r.Id).SingleAsync(cancellationToken);

    /// <summary>Uitnodiging voor een jurylid. Concepttekst; de definitieve tekst keurt het bestuur goed.</summary>
    public static EmailMessage InviteMail(string to, string name)
    {
        const string Subject = "Je bent uitgenodigd als jurylid voor de optocht van De Vrolijke Drammers";
        var text = $"""
            Beste {name},

            Je bent uitgenodigd als jurylid voor de optocht van De Vrolijke Drammers. Je jureert in de app.

            Zo log je de eerste keer in:
            1. Installeer de app van De Vrolijke Drammers (App Store of Google Play) en kies Meer → Inloggen.
            2. Kies op de inlogpagina voor een nieuw account ("Maak er een") en vul dit e-mailadres in: {to}.
            3. Je krijgt een code per e-mail. Vul die in; een wachtwoord is niet nodig.

            Daarna zie je in de app onder Optocht de knop Jureren. Welke categorieën je beoordeelt, hoor je van het bestuur of de hoofdjury.

            Groeten,
            De Vrolijke Drammers
            """;
        var n = System.Net.WebUtility.HtmlEncode(name);
        var t = System.Net.WebUtility.HtmlEncode(to);
        var html = $"""
            <p>Beste {n},</p>
            <p>Je bent uitgenodigd als jurylid voor de optocht van De Vrolijke Drammers. Je jureert in de app.</p>
            <p><strong>Zo log je de eerste keer in:</strong></p>
            <ol><li>Installeer de app van De Vrolijke Drammers (App Store of Google Play) en kies <em>Meer → Inloggen</em>.</li><li>Kies op de inlogpagina voor een nieuw account (&quot;Maak er een&quot;) en vul dit e-mailadres in: {t}.</li><li>Je krijgt een code per e-mail. Vul die in; een wachtwoord is niet nodig.</li></ol>
            <p>Daarna zie je in de app onder <em>Optocht</em> de knop <em>Jureren</em>. Welke categorieën je beoordeelt, hoor je van het bestuur of de hoofdjury.</p>
            <p>Groeten,<br>De Vrolijke Drammers</p>
            """;
        return new EmailMessage(to, Subject, text, html);
    }
}
