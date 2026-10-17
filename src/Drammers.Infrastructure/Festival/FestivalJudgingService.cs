using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Content.Festival;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Festival;

public sealed record FestivalJurorView(Guid UserId, int Number, string Name, string Email, bool Active, bool HeadJury, int SubmittedCategories);

public sealed record FestivalScoreInput(Guid EntryId, FestivalCriterion Criterion, int Value, DateTime ScoredAt);

public sealed record FestivalCommentInput(Guid EntryId, string? Text);

public sealed record FestivalScoreView(Guid EntryId, FestivalCriterion Criterion, int Value, DateTime ScoredAt);

public sealed record FestivalCommentView(Guid EntryId, string Text);

public sealed record JuryDance(Guid EntryId, int Number, string Club, string Names, string? Title, int DancerCount, decimal AverageAge);

/// <summary>Een programmacategorie voor het jurylid: ingediend, aan de beurt (de eerste die nog niet is ingediend) of later.</summary>
public sealed record JuryCategory(string Key, string Name, bool Solo, int Dances, string Start, bool Submitted, bool Current);

/// <summary>Alles voor de app van het jurylid: alleen de dansen van de categorie die aan de beurt is.</summary>
public sealed record FestivalJurySession(
    Guid FestivalId, string FestivalName, DateOnly FestivalDate, string Location, int JurorNumber, bool HeadJury,
    IReadOnlyList<JuryCategory> Categories, JuryCategory? Current, IReadOnlyList<JuryDance> Dances,
    IReadOnlyList<FestivalScoreView> Scores, IReadOnlyList<FestivalCommentView> Comments);

/// <summary>Een dans in de uitslag: punten per onderdeel (som over de juryleden) en per jurylid.</summary>
public sealed record ResultRow(
    Guid EntryId, Guid RegistrationId, int Place, string Club, string Names, string? Title, int Synchronicity, int Choreography, int Execution,
    int Difficulty, int Total, IReadOnlyList<int?> PerJuror);

public sealed record ResultCategory(string Key, string Name, int Part, bool Solo, int Jurors, int Submitted, bool Complete, IReadOnlyList<ResultRow> Rows);

public sealed record FestivalResults(
    Guid FestivalId, string FestivalName, DateOnly FestivalDate, string Location, IReadOnlyList<FestivalJurorView> Jurors,
    IReadOnlyList<ResultCategory> Categories, bool Complete, DateTime? HeadJuryPublishedAt, DateTime? AwardsCompletedAt, string? AwardsCompletedBy);

public sealed record HeadJuryPart(int Part, string Name, IReadOnlyList<ResultCategory> Categories);

/// <summary>De uitslag voor de hoofdjury: per deel van de prijsuitreiking, per categorie van de laatste plaats naar de winnaar.</summary>
public sealed record HeadJuryView(bool Published, string FestivalName, IReadOnlyList<HeadJuryPart> Parts, DateTime? AwardsCompletedAt);

/// <summary>
/// De jury van het Dansgardefestival (2026-10-15). Juryleden zijn eigen accounts (rol Jury dansgardefestival) met een vast
/// nummer; ze beoordelen alle dansen in programmavolgorde. De app toont alleen de categorie die aan de beurt is: de eerste
/// die het jurylid nog niet heeft ingediend. Indienen kan pas als alle dansen van de categorie vier onderdelen hebben.
/// Totaal = de som van alle onderdelen van alle juryleden; bij gelijk totaal wint de hoogste Uitvoering, anders een
/// gedeelde plaats (nog te bevestigen door de organisatie).
/// </summary>
public sealed class FestivalJudgingService(
    DrammersDbContext db,
    FestivalProgramService program,
    MemberAccounts accounts,
    AccountAdministration administration,
    IEmailSender email,
    IAuditLogger audit,
    IClock clock)
{
    public const int MaxScore = 10;

    public const int MaxCommentLength = 1000;

    public static readonly IReadOnlyList<FestivalCriterion> Criteria =
        [FestivalCriterion.Synchronicity, FestivalCriterion.Choreography, FestivalCriterion.Execution, FestivalCriterion.Difficulty];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DateTime Now => clock.UtcNow.UtcDateTime;

    // ---- Juryleden (portal) ----------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<FestivalJurorView>> JurorsAsync(CancellationToken cancellationToken)
    {
        var festival = await program.ActiveFestivalAsync(cancellationToken);
        return await JurorsAsync(festival, cancellationToken);
    }

    public async Task<Guid> InviteAsync(string name, string emailAddress, bool headJury, CancellationToken cancellationToken)
    {
        var cleanName = name.Trim();
        var address = emailAddress.Trim().ToLowerInvariant();
        if (cleanName.Length is 0 or > 100 || address.Length is 0 or > 254 || !address.Contains('@', StringComparison.Ordinal))
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een naam (hooguit 100 tekens) en een geldig e-mailadres in.");
        }

        var festival = await program.ActiveFestivalAsync(cancellationToken);
        var userId = await accounts.EnsureInvitedAccountAsync(address, cleanName, DefaultRoles.JuryDansgardefestival, cancellationToken);
        if (headJury)
        {
            await SetHeadJuryAsync(userId, true, cancellationToken);
        }

        await EnsureNumberAsync(festival.Id, userId, cancellationToken);
        await audit.WriteAsync(new AuditEntry("festival.jury.invited", "User", userId.ToString(), null, JsonSerializer.Serialize(new { name = cleanName, email = address, headJury }, Json)), cancellationToken);
        await email.SendAsync(InviteMail(address, cleanName), cancellationToken);
        return userId;
    }

    public async Task SetHeadJuryAsync(Guid userId, bool headJury, CancellationToken cancellationToken)
    {
        await JurorUserAsync(userId, cancellationToken);
        var roles = await RolesOfAsync(userId, cancellationToken);
        if (roles.Any(r => r.RoleCode == DefaultRoles.HoofdjuryDansgardefestival) == headJury)
        {
            return;
        }

        await administration.SetUserRolesAsync(userId,
            headJury ? [.. roles, new RoleAssignment(DefaultRoles.HoofdjuryDansgardefestival)] : [.. roles.Where(r => r.RoleCode != DefaultRoles.HoofdjuryDansgardefestival)],
            cancellationToken);
    }

    /// <summary>Haalt de juryrollen weg; het nummer blijft gereserveerd (al ingevulde punten houden hun jurylid).</summary>
    public async Task RemoveAsync(Guid userId, CancellationToken cancellationToken)
    {
        await JurorUserAsync(userId, cancellationToken);
        var roles = await RolesOfAsync(userId, cancellationToken);
        await administration.SetUserRolesAsync(userId,
            [.. roles.Where(r => r.RoleCode is not (DefaultRoles.JuryDansgardefestival or DefaultRoles.HoofdjuryDansgardefestival))], cancellationToken);
    }

    public async Task ResendInviteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await JurorUserAsync(userId, cancellationToken);
        await email.SendAsync(InviteMail(user.Email, user.DisplayName), cancellationToken);
    }

    // ---- App van het jurylid ---------------------------------------------------------------------------------------

    public async Task<FestivalJurySession> SessionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var festival = await program.ActiveFestivalAsync(cancellationToken);
        var number = await EnsureNumberAsync(festival.Id, userId, cancellationToken);
        var categories = await program.CategoriesAsync(festival.Id, cancellationToken);
        var submitted = await db.FestivalCategorySubmissions.AsNoTracking().Where(s => s.FestivalId == festival.Id && s.UserId == userId)
            .Select(s => s.CategoryKey).ToListAsync(cancellationToken);
        var currentKey = categories.FirstOrDefault(c => !submitted.Contains(c.CategoryKey!))?.CategoryKey;
        var list = categories.Select(c => new JuryCategory(c.CategoryKey!, c.Title, c.Solo, c.Dances.Count, c.Start, submitted.Contains(c.CategoryKey!), c.CategoryKey == currentKey)).ToList();
        var current = categories.FirstOrDefault(c => c.CategoryKey == currentKey);
        var dances = current?.Dances.Select(d => new JuryDance(d.EntryId, d.Number, d.Club, d.Names, d.Title, d.DancerCount, d.AverageAge)).ToList() ?? [];
        var ids = dances.Select(d => d.EntryId).ToList();
        var scores = await db.FestivalScores.AsNoTracking().Where(s => s.UserId == userId && ids.Contains(s.EntryId))
            .Select(s => new FestivalScoreView(s.EntryId, s.Criterion, s.Value, s.ScoredAt)).ToListAsync(cancellationToken);
        var comments = await db.FestivalComments.AsNoTracking().Where(c => c.UserId == userId && ids.Contains(c.EntryId))
            .Select(c => new FestivalCommentView(c.EntryId, c.Text)).ToListAsync(cancellationToken);
        var head = (await RolesOfAsync(userId, cancellationToken)).Any(r => r.RoleCode == DefaultRoles.HoofdjuryDansgardefestival);
        return new FestivalJurySession(festival.Id, festival.Name, festival.FestivalDate, festival.Location, number, head, list,
            list.FirstOrDefault(c => c.Current), dances, scores, comments);
    }

    /// <summary>Punten en opmerkingen (ook als ze later binnenkomen, bijv. zonder netwerk); per onderdeel wint de nieuwste.</summary>
    public async Task SaveAsync(Guid userId, IReadOnlyList<FestivalScoreInput> scores, IReadOnlyList<FestivalCommentInput> comments, CancellationToken cancellationToken)
    {
        var festival = await program.ActiveFestivalAsync(cancellationToken);
        await EnsureNumberAsync(festival.Id, userId, cancellationToken);
        if (scores.Any(s => s.Value is < 0 or > MaxScore || !Enum.IsDefined(s.Criterion)))
        {
            throw new DomainException(ErrorCodes.Validation, $"Punten zijn 0 tot en met {MaxScore}.");
        }

        if (comments.Any(c => c.Text?.Length > MaxCommentLength))
        {
            throw new DomainException(ErrorCodes.Validation, $"Een opmerking is hooguit {MaxCommentLength} tekens.");
        }

        // Alleen dansen van dit festival in een categorie die dit jurylid nog niet heeft ingediend.
        var categories = await program.CategoriesAsync(festival.Id, cancellationToken);
        var submitted = await db.FestivalCategorySubmissions.Where(s => s.FestivalId == festival.Id && s.UserId == userId).Select(s => s.CategoryKey).ToListAsync(cancellationToken);
        var open = categories.Where(c => !submitted.Contains(c.CategoryKey!)).SelectMany(c => c.Dances).Select(d => d.EntryId).ToHashSet();
        var all = categories.SelectMany(c => c.Dances).Select(d => d.EntryId).ToHashSet();
        var touched = scores.Select(s => s.EntryId).Concat(comments.Select(c => c.EntryId)).Distinct().ToList();
        if (touched.Any(id => !all.Contains(id)))
        {
            throw new DomainException(ErrorCodes.Validation, "Onbekende dans voor dit festival.");
        }

        if (touched.Any(id => !open.Contains(id)))
        {
            throw new DomainException(ErrorCodes.Validation, "Deze categorie is al ingediend; daar kun je niets meer wijzigen.", DomainErrorKind.Conflict);
        }

        var existing = await db.FestivalScores.Where(s => s.UserId == userId && touched.Contains(s.EntryId)).ToListAsync(cancellationToken);
        foreach (var input in scores.GroupBy(s => (s.EntryId, s.Criterion)).Select(g => g.MaxBy(s => s.ScoredAt)!))
        {
            var scoredAt = input.ScoredAt > Now.AddMinutes(5) ? Now : input.ScoredAt.ToUniversalTime();
            var score = existing.SingleOrDefault(s => s.EntryId == input.EntryId && s.Criterion == input.Criterion);
            if (score is null)
            {
                db.FestivalScores.Add(new FestivalScore
                {
                    FestivalId = festival.Id,
                    EntryId = input.EntryId,
                    UserId = userId,
                    Criterion = input.Criterion,
                    Value = input.Value,
                    ScoredAt = scoredAt,
                    ReceivedAt = Now,
                });
            }
            else if (scoredAt >= score.ScoredAt)
            {
                (score.Value, score.ScoredAt, score.ReceivedAt) = (input.Value, scoredAt, Now);
            }
        }

        var existingComments = await db.FestivalComments.Where(c => c.UserId == userId && touched.Contains(c.EntryId)).ToListAsync(cancellationToken);
        foreach (var input in comments)
        {
            var text = input.Text?.Trim();
            var comment = existingComments.SingleOrDefault(c => c.EntryId == input.EntryId);
            if (string.IsNullOrEmpty(text))
            {
                if (comment is not null)
                {
                    db.FestivalComments.Remove(comment);
                }
            }
            else if (comment is null)
            {
                db.FestivalComments.Add(new FestivalComment { FestivalId = festival.Id, EntryId = input.EntryId, UserId = userId, Text = text, WrittenAt = Now });
            }
            else
            {
                (comment.Text, comment.WrittenAt) = (text, Now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Een categorie indienen: alle dansen moeten vier onderdelen hebben. Daarna komt de volgende categorie.</summary>
    public async Task SubmitCategoryAsync(Guid userId, string categoryKey, CancellationToken cancellationToken)
    {
        var festival = await program.ActiveFestivalAsync(cancellationToken);
        var category = (await program.CategoriesAsync(festival.Id, cancellationToken)).SingleOrDefault(c => c.CategoryKey == categoryKey)
            ?? throw new DomainException(ErrorCodes.NotFound, "Categorie niet gevonden.", DomainErrorKind.NotFound);
        if (await db.FestivalCategorySubmissions.AnyAsync(s => s.FestivalId == festival.Id && s.CategoryKey == categoryKey && s.UserId == userId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "Deze categorie is al ingediend.", DomainErrorKind.Conflict);
        }

        var ids = category.Dances.Select(d => d.EntryId).ToList();
        var counts = await db.FestivalScores.Where(s => s.UserId == userId && ids.Contains(s.EntryId)).GroupBy(s => s.EntryId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var missing = category.Dances.Where(d => counts.GetValueOrDefault(d.EntryId) < Criteria.Count).Select(d => d.Names).ToList();
        if (missing.Count > 0)
        {
            throw new DomainException(ErrorCodes.Validation, $"Nog niet alle onderdelen ingevuld: {string.Join(", ", missing)}.");
        }

        db.FestivalCategorySubmissions.Add(new FestivalCategorySubmission { FestivalId = festival.Id, CategoryKey = categoryKey, UserId = userId, SubmittedAt = Now });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("festival.jury.category-submitted", "DanceFestival", festival.Id.ToString(), null, categoryKey), cancellationToken);
    }

    // ---- Uitslag ---------------------------------------------------------------------------------------------------

    public async Task<FestivalResults> ResultsAsync(CancellationToken cancellationToken)
    {
        var festival = await program.ActiveFestivalAsync(cancellationToken);
        return await ResultsAsync(festival, cancellationToken);
    }

    internal async Task<FestivalResults> ResultsAsync(DanceFestival festival, CancellationToken cancellationToken)
    {
        var jurors = await JurorsAsync(festival, cancellationToken);
        var active = jurors.Where(j => j.Active).ToList();
        var numbers = await db.FestivalJurors.AsNoTracking().Where(j => j.FestivalId == festival.Id).ToDictionaryAsync(j => j.UserId, j => j.Number, cancellationToken);
        var maxNumber = numbers.Count == 0 ? 0 : numbers.Values.Max();
        var categories = await program.CategoriesAsync(festival.Id, cancellationToken);
        var scores = await db.FestivalScores.AsNoTracking().Where(s => s.FestivalId == festival.Id).ToListAsync(cancellationToken);
        var submissions = await db.FestivalCategorySubmissions.AsNoTracking().Where(s => s.FestivalId == festival.Id).ToListAsync(cancellationToken);
        var activeIds = active.Select(j => j.UserId).ToHashSet();
        var result = new List<ResultCategory>();
        foreach (var category in categories)
        {
            var rows = category.Dances.Select(d =>
            {
                var own = scores.Where(s => s.EntryId == d.EntryId).ToList();
                int Sum(FestivalCriterion c) => own.Where(s => s.Criterion == c).Sum(s => s.Value);
                var perJuror = Enumerable.Range(1, maxNumber).Select(n =>
                {
                    var user = numbers.FirstOrDefault(x => x.Value == n).Key;
                    var mine = own.Where(s => s.UserId == user).ToList();
                    return mine.Count == 0 ? (int?)null : mine.Sum(s => s.Value);
                }).ToList();
                return new ResultRow(d.EntryId, d.RegistrationId, 0, d.Club, d.Names, d.Title, Sum(FestivalCriterion.Synchronicity),
                    Sum(FestivalCriterion.Choreography), Sum(FestivalCriterion.Execution), Sum(FestivalCriterion.Difficulty), own.Sum(s => s.Value), perJuror);
            }).ToList();
            var ranked = Rank(rows);
            var done = submissions.Count(s => s.CategoryKey == category.CategoryKey && activeIds.Contains(s.UserId));
            result.Add(new ResultCategory(category.CategoryKey!, category.Title, PartOf(category.CategoryKey!), category.Solo, active.Count, done,
                active.Count > 0 && done >= active.Count, ranked));
        }

        return new FestivalResults(festival.Id, festival.Name, festival.FestivalDate, festival.Location, jurors, result,
            result.Count > 0 && result.All(c => c.Complete), festival.HeadJuryPublishedAt, festival.AwardsCompletedAt, festival.AwardsCompletedBy);
    }

    /// <summary>Hoogste totaal eerst; gelijk totaal: hoogste Uitvoering; nog steeds gelijk: gedeelde plaats.</summary>
    public static IReadOnlyList<ResultRow> Rank(IReadOnlyList<ResultRow> rows)
    {
        var ordered = rows.OrderByDescending(r => r.Total).ThenByDescending(r => r.Execution).ThenBy(r => r.Names, StringComparer.Ordinal).ToList();
        var ranked = new List<ResultRow>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var place = i > 0 && ordered[i].Total == ordered[i - 1].Total && ordered[i].Execution == ordered[i - 1].Execution ? ranked[i - 1].Place : i + 1;
            ranked.Add(ordered[i] with { Place = place });
        }

        return ranked;
    }

    /// <summary>Delen van de prijsuitreiking: 1 minioren, 2 junioren, 3 jeugd en hoofdklasse (solo en duo bij hun leeftijd).</summary>
    public static int PartOf(string categoryKey) => categoryKey.Split('|')[0] switch
    {
        nameof(DanceCategory.Minioren) => 1,
        nameof(DanceCategory.Junioren) => 2,
        _ => 3,
    };

    public static string PartName(int part) => part switch
    {
        1 => "Deel 1 · Minioren",
        2 => "Deel 2 · Junioren",
        _ => "Deel 3 · Jeugd en hoofdklasse",
    };

    /// <summary>De uitslag naar de hoofdjury. Zonder <paramref name="force"/> alleen als alle juryleden alles hebben ingediend.</summary>
    public async Task PublishToHeadJuryAsync(bool force, CancellationToken cancellationToken)
    {
        var festival = await program.ActiveFestivalAsync(cancellationToken);
        var results = await ResultsAsync(festival, cancellationToken);
        if (!results.Complete && !force)
        {
            throw new DomainException(ErrorCodes.Validation, "Nog niet alle juryleden hebben alle categorieën ingediend.", DomainErrorKind.Conflict);
        }

        festival.HeadJuryPublishedAt = Now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("festival.results.head-jury", "DanceFestival", festival.Id.ToString(), null, results.Complete ? "compleet" : "onvolledig"), cancellationToken);
    }

    public async Task<HeadJuryView> HeadJuryAsync(CancellationToken cancellationToken)
    {
        var festival = await program.ActiveFestivalAsync(cancellationToken);
        if (festival.HeadJuryPublishedAt is null)
        {
            return new HeadJuryView(false, festival.Name, [], festival.AwardsCompletedAt);
        }

        var results = await ResultsAsync(festival, cancellationToken);
        var parts = results.Categories.GroupBy(c => c.Part).OrderBy(g => g.Key)
            .Select(g => new HeadJuryPart(g.Key, PartName(g.Key), [.. g.Select(c => c with { Rows = [.. c.Rows.Reverse()] })])).ToList();
        return new HeadJuryView(true, festival.Name, parts, festival.AwardsCompletedAt);
    }

    /// <summary>Prijsuitreiking afgerond (of weer terug): door de hoofdjury in de app, of overruled door de organisatie.</summary>
    public async Task SetAwardsCompletedAsync(bool completed, bool byOrganisation, CancellationToken cancellationToken)
    {
        var festival = await program.ActiveFestivalAsync(cancellationToken);
        if (!byOrganisation && festival.HeadJuryPublishedAt is null)
        {
            throw new DomainException(ErrorCodes.Validation, "De uitslag is nog niet naar de hoofdjury gestuurd.", DomainErrorKind.Conflict);
        }

        festival.AwardsCompletedAt = completed ? Now : null;
        festival.AwardsCompletedBy = completed ? (byOrganisation ? "organisatie" : "hoofdjury") : null;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("festival.awards", "DanceFestival", festival.Id.ToString(), null, $"{completed} ({festival.AwardsCompletedBy})"), cancellationToken);
    }

    // ---- Hulp ------------------------------------------------------------------------------------------------------

    private async Task<IReadOnlyList<FestivalJurorView>> JurorsAsync(DanceFestival festival, CancellationToken cancellationToken)
    {
        var juryRole = await RoleIdAsync(DefaultRoles.JuryDansgardefestival, cancellationToken);
        var headRole = await RoleIdAsync(DefaultRoles.HoofdjuryDansgardefestival, cancellationToken);
        var users = await db.Users.AsNoTracking()
            .Where(u => u.AccountStatus != AccountStatus.Deleted && u.Roles.Any(r => r.RoleId == juryRole))
            .Select(u => new { u.Id, u.DisplayName, u.Email, Head = u.Roles.Any(r => r.RoleId == headRole) }).ToListAsync(cancellationToken);
        foreach (var u in users)
        {
            await EnsureNumberAsync(festival.Id, u.Id, cancellationToken);
        }

        var numbers = await db.FestivalJurors.AsNoTracking().Where(j => j.FestivalId == festival.Id).ToListAsync(cancellationToken);
        var submitted = await db.FestivalCategorySubmissions.AsNoTracking().Where(s => s.FestivalId == festival.Id).GroupBy(s => s.UserId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var userIds = numbers.Select(n => n.UserId).ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => (u.DisplayName, u.Email), cancellationToken);
        return [.. numbers.OrderBy(n => n.Number).Select(n =>
        {
            var user = users.SingleOrDefault(u => u.Id == n.UserId);
            var (name, mail) = names.GetValueOrDefault(n.UserId, ("(verwijderd)", ""));
            return new FestivalJurorView(n.UserId, n.Number, name, mail, user is not null, user?.Head ?? false, submitted.GetValueOrDefault(n.UserId));
        })];
    }

    /// <summary>Vast nummer per festival (Jurylid 1, 2, 3 …), op volgorde van binnenkomst.</summary>
    private async Task<int> EnsureNumberAsync(Guid festivalId, Guid userId, CancellationToken cancellationToken)
    {
        var existing = await db.FestivalJurors.AsNoTracking().SingleOrDefaultAsync(j => j.FestivalId == festivalId && j.UserId == userId, cancellationToken);
        if (existing is not null)
        {
            return existing.Number;
        }

        var next = (await db.FestivalJurors.Where(j => j.FestivalId == festivalId).MaxAsync(j => (int?)j.Number, cancellationToken) ?? 0) + 1;
        db.FestivalJurors.Add(new FestivalJuror { FestivalId = festivalId, UserId = userId, Number = next, AddedAt = Now });
        await db.SaveChangesAsync(cancellationToken);
        return next;
    }

    private async Task<User> JurorUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var juryRole = await RoleIdAsync(DefaultRoles.JuryDansgardefestival, cancellationToken);
        return await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId && u.AccountStatus != AccountStatus.Deleted && u.Roles.Any(r => r.RoleId == juryRole), cancellationToken)
            ?? throw new DomainException(ErrorCodes.UserNotFound, "Jurylid niet gevonden.", DomainErrorKind.NotFound);
    }

    private async Task<List<RoleAssignment>> RolesOfAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.UserRoles.AsNoTracking().Where(r => r.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new RoleAssignment(r.Code, ur.ValidFrom, ur.ValidTo))
            .ToListAsync(cancellationToken);

    private async Task<int> RoleIdAsync(string code, CancellationToken cancellationToken) =>
        await db.Roles.Where(r => r.Code == code).Select(r => r.Id).SingleAsync(cancellationToken);

    public static EmailMessage InviteMail(string to, string name)
    {
        const string Subject = "Je bent uitgenodigd als jurylid voor het Dansgardefestival van De Vrolijke Drammers";
        var text = $"""
            Beste {name},

            Je bent uitgenodigd als jurylid voor het Dansgardefestival van De Vrolijke Drammers. Je jureert in de app.

            Zo log je de eerste keer in:
            1. Installeer de app van De Vrolijke Drammers (App Store of Google Play) en kies Meer → Inloggen.
            2. Kies op de inlogpagina voor een nieuw account ("Maak er een") en vul dit e-mailadres in: {to}.
            3. Je krijgt een code per e-mail. Vul die in; een wachtwoord is niet nodig.

            Daarna zie je in de app onder Meer de knop Jureren dansgardefestival. Op de dag zelf zie je steeds de categorie die op het podium staat.

            Groeten,
            De organisatie van het Dansgardefestival
            """;
        var n = System.Net.WebUtility.HtmlEncode(name);
        var t = System.Net.WebUtility.HtmlEncode(to);
        var html = $"""
            <p>Beste {n},</p>
            <p>Je bent uitgenodigd als jurylid voor het Dansgardefestival van De Vrolijke Drammers. Je jureert in de app.</p>
            <p><strong>Zo log je de eerste keer in:</strong></p>
            <ol><li>Installeer de app van De Vrolijke Drammers (App Store of Google Play) en kies <em>Meer → Inloggen</em>.</li><li>Kies op de inlogpagina voor een nieuw account (&quot;Maak er een&quot;) en vul dit e-mailadres in: {t}.</li><li>Je krijgt een code per e-mail. Vul die in; een wachtwoord is niet nodig.</li></ol>
            <p>Daarna zie je in de app onder <em>Meer</em> de knop <em>Jureren dansgardefestival</em>. Op de dag zelf zie je steeds de categorie die op het podium staat.</p>
            <p>Groeten,<br>De organisatie van het Dansgardefestival</p>
            """;
        return new EmailMessage(to, Subject, text, html);
    }
}
