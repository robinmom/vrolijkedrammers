using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Parade.Judging;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.ParadeManagement;

public sealed record JudgingEntry(Guid RegistrationId, int? StartNumber, string GroupName, string? Motto, int CategoryId, string CategoryName, bool Assigned);

public sealed record JudgingScoreRow(Guid RegistrationId, int Pass, JudgingCriterion Criterion, int Value, DateTime ScoredAt);

public sealed record JudgingCategoryRef(int Id, string Name);

public sealed record JurorSession(
    Guid ParadeId, string ParadeName, DateOnly ParadeDate, TimeOnly StartTime, DateTime? SubmittedAt,
    IReadOnlyList<JudgingCategoryRef> Categories, IReadOnlyList<JudgingEntry> Entries, IReadOnlyList<JudgingScoreRow> Scores);

public sealed record ScoreInput(Guid RegistrationId, int Pass, JudgingCriterion Criterion, int Value, DateTime ScoredAt);

public sealed record JurorProgress(Guid UserId, DateTime? SubmittedAt, int Scored, int Assigned);

public sealed record OutsideScore(Guid UserId, string JurorName, Guid RegistrationId, int? StartNumber, string GroupName, string CategoryName, int Passes, OutsideDecision? Decision);

public sealed record OutsideDecisionInput(Guid UserId, Guid RegistrationId, OutsideDecision? Decision);

/// <summary>
/// Jureren in de app (fase 22b). Een jurylid ziet de inzendingen van de huidige optocht in startvolgorde, met per
/// inzending of die in zijn categorieën valt. Scores (3 passages × 4 criteria, 0–100) komen in porties binnen, ook later
/// vanaf een toestel zonder netwerk: per score wint de nieuwste invulling. Na indienen is niets meer te wijzigen.
/// Beoordelingen buiten de eigen categorieën tellen alleen mee na akkoord van het bestuur of de hoofdjury; die zien
/// daarbij nooit de scores zelf.
/// </summary>
public sealed class ParadeJudging(DrammersDbContext db, IAuditLogger audit, ICurrentActor actor, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Toestellen met een verkeerde klok kunnen hun scores niet "in de toekomst" laten winnen.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public const int MaxBatch = 1000;

    private DateTime Now => clock.UtcNow.UtcDateTime;

    public async Task<JurorSession> SessionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var parade = await db.CurrentParades().AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Er is (nog) geen optocht.", DomainErrorKind.NotFound);
        var assigned = await db.ParadeJurorAssignments.AsNoTracking().Where(a => a.ParadeId == parade.Id && a.UserId == userId)
            .Select(a => a.CategoryId).ToListAsync(cancellationToken);
        var categories = await db.ParadeCategories.AsNoTracking().Where(c => assigned.Contains(c.Id)).OrderBy(c => c.SortOrder)
            .Select(c => new JudgingCategoryRef(c.Id, c.Name)).ToListAsync(cancellationToken);
        var entries = await EntriesAsync(parade.Id, cancellationToken);
        var scores = await db.JudgingScores.AsNoTracking().Where(s => s.ParadeId == parade.Id && s.UserId == userId)
            .Select(s => new JudgingScoreRow(s.RegistrationId, s.Pass, s.Criterion, s.Value, s.ScoredAt)).ToListAsync(cancellationToken);
        var submitted = await db.JudgingSubmissions.AsNoTracking().Where(s => s.ParadeId == parade.Id && s.UserId == userId)
            .Select(s => (DateTime?)s.SubmittedAt).FirstOrDefaultAsync(cancellationToken);
        return new JurorSession(parade.Id, parade.Name, parade.ParadeDate, parade.StartTime, submitted, categories,
            [.. entries.Select(e => e with { Assigned = assigned.Contains(e.CategoryId) })], scores);
    }

    /// <summary>De inzendingen die beoordeeld worden: ingediend, niet ingetrokken of afgewezen, in een beoordeelde categorie.</summary>
    public async Task<List<JudgingEntry>> EntriesAsync(Guid paradeId, CancellationToken cancellationToken) =>
        await (
            from r in db.ParadeRegistrations.AsNoTracking()
            join j in db.ParadeJudgingCategories.AsNoTracking() on new { r.ParadeId, CategoryId = r.CategoryId!.Value } equals new { j.ParadeId, j.CategoryId }
            join c in db.ParadeCategories.AsNoTracking() on j.CategoryId equals c.Id
            where r.ParadeId == paradeId && j.Judged && r.RegistrationNumber != null
                && r.Status != RegistrationStatus.Draft && r.Status != RegistrationStatus.Withdrawn && r.Status != RegistrationStatus.Rejected
            orderby r.StartNumber == null, r.StartNumber, r.RegistrationNumber
            select new JudgingEntry(r.Id, r.StartNumber, r.GroupName ?? "", r.Subject, c.Id, c.Name, false))
        .ToListAsync(cancellationToken);

    /// <summary>Scores opslaan of bijwerken; per score wint de nieuwste invulling. Geeft het aantal gewijzigde scores.</summary>
    public async Task<int> SaveScoresAsync(Guid userId, Guid paradeId, IReadOnlyList<ScoreInput> scores, CancellationToken cancellationToken)
    {
        if (scores.Count > MaxBatch)
        {
            throw new DomainException(ErrorCodes.Validation, $"Hooguit {MaxBatch} scores per keer.");
        }

        if (scores.Any(s => s.Value is < 0 or > 100 || s.Pass is < 1 or > JudgingScore.Passes || !Enum.IsDefined(s.Criterion)))
        {
            throw new DomainException(ErrorCodes.Validation, "Een score is 0 tot en met 100, voor passage 1, 2 of 3.");
        }

        await EnsureNotSubmittedAsync(paradeId, userId, cancellationToken);
        var entryIds = (await EntriesAsync(paradeId, cancellationToken)).Select(e => e.RegistrationId).ToHashSet();
        if (scores.Any(s => !entryIds.Contains(s.RegistrationId)))
        {
            throw new DomainException(ErrorCodes.Validation, "Deze inzending wordt in deze optocht niet beoordeeld.");
        }

        var ids = scores.Select(s => s.RegistrationId).Distinct().ToList();
        var existing = await db.JudgingScores.Where(s => s.UserId == userId && ids.Contains(s.RegistrationId)).ToListAsync(cancellationToken);
        var changed = 0;
        foreach (var input in scores.GroupBy(s => (s.RegistrationId, s.Pass, s.Criterion)).Select(g => g.MaxBy(s => s.ScoredAt)!))
        {
            var scoredAt = DateTime.SpecifyKind(input.ScoredAt, DateTimeKind.Utc);
            scoredAt = scoredAt > Now + MaxClockSkew ? Now : scoredAt;
            var score = existing.SingleOrDefault(s => s.RegistrationId == input.RegistrationId && s.Pass == input.Pass && s.Criterion == input.Criterion);
            if (score is null)
            {
                db.JudgingScores.Add(new JudgingScore
                {
                    ParadeId = paradeId,
                    RegistrationId = input.RegistrationId,
                    UserId = userId,
                    Pass = input.Pass,
                    Criterion = input.Criterion,
                    Value = input.Value,
                    ScoredAt = scoredAt,
                    ReceivedAt = Now,
                });
                changed++;
            }
            else if (score.ScoredAt < scoredAt && score.Value != input.Value)
            {
                (score.Value, score.ScoredAt, score.ReceivedAt) = (input.Value, scoredAt, Now);
                changed++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    /// <summary>Indienen (idempotent); daarna kan het jurylid voor deze optocht niets meer wijzigen.</summary>
    public async Task<DateTime> SubmitAsync(Guid userId, Guid paradeId, CancellationToken cancellationToken)
    {
        var existing = await db.JudgingSubmissions.AsNoTracking().SingleOrDefaultAsync(s => s.ParadeId == paradeId && s.UserId == userId, cancellationToken);
        if (existing is not null)
        {
            return existing.SubmittedAt;
        }

        if (!await db.Parades.AnyAsync(p => p.Id == paradeId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.ParadeNotFound, "Optocht niet gevonden.", DomainErrorKind.NotFound);
        }

        db.JudgingSubmissions.Add(new JudgingSubmission { ParadeId = paradeId, UserId = userId, SubmittedAt = Now });
        await db.SaveChangesAsync(cancellationToken);
        var count = await db.JudgingScores.CountAsync(s => s.ParadeId == paradeId && s.UserId == userId, cancellationToken);
        await audit.WriteAsync(new AuditEntry("jury.submitted", "Parade", paradeId.ToString(), null, JsonSerializer.Serialize(new { userId, scores = count }, Json)), cancellationToken);
        return Now;
    }

    private async Task EnsureNotSubmittedAsync(Guid paradeId, Guid userId, CancellationToken cancellationToken)
    {
        if (await db.JudgingSubmissions.AnyAsync(s => s.ParadeId == paradeId && s.UserId == userId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.JudgingSubmitted, "Je jurering is al ingediend; wijzigen kan niet meer.", DomainErrorKind.Conflict);
        }
    }

    // ----- Portal: voortgang en beoordelingen buiten categorie --------------------------------------------------------

    /// <summary>Per jurylid: ingediend, en hoeveel van de toegewezen inzendingen al (deels) beoordeeld zijn.</summary>
    public async Task<IReadOnlyList<JurorProgress>> ProgressAsync(Guid paradeId, CancellationToken cancellationToken)
    {
        var entries = await EntriesAsync(paradeId, cancellationToken);
        var assignments = await db.ParadeJurorAssignments.AsNoTracking().Where(a => a.ParadeId == paradeId).ToListAsync(cancellationToken);
        var scored = await db.JudgingScores.AsNoTracking().Where(s => s.ParadeId == paradeId).Select(s => new { s.UserId, s.RegistrationId }).Distinct().ToListAsync(cancellationToken);
        var submissions = await db.JudgingSubmissions.AsNoTracking().Where(s => s.ParadeId == paradeId).ToListAsync(cancellationToken);
        return [.. assignments.Select(a => a.UserId).Concat(submissions.Select(s => s.UserId)).Distinct().Select(userId =>
        {
            var mine = entries.Where(e => assignments.Any(a => a.UserId == userId && a.CategoryId == e.CategoryId)).Select(e => e.RegistrationId).ToHashSet();
            return new JurorProgress(userId, submissions.SingleOrDefault(s => s.UserId == userId)?.SubmittedAt,
                scored.Count(s => s.UserId == userId && mine.Contains(s.RegistrationId)), mine.Count);
        })];
    }

    /// <summary>Beoordelingen buiten de categorieën van het jurylid, met hoeveel passages en de beslissing (zonder scores).</summary>
    public async Task<IReadOnlyList<OutsideScore>> OutsideAsync(Guid paradeId, CancellationToken cancellationToken)
    {
        var entries = (await EntriesAsync(paradeId, cancellationToken)).ToDictionary(e => e.RegistrationId);
        var assignments = await db.ParadeJurorAssignments.AsNoTracking().Where(a => a.ParadeId == paradeId).ToListAsync(cancellationToken);
        var scored = await db.JudgingScores.AsNoTracking().Where(s => s.ParadeId == paradeId)
            .GroupBy(s => new { s.UserId, s.RegistrationId })
            .Select(g => new { g.Key.UserId, g.Key.RegistrationId, Passes = g.Select(s => s.Pass).Distinct().Count() })
            .ToListAsync(cancellationToken);
        var reviews = await db.JudgingOutsideReviews.AsNoTracking().Where(r => r.ParadeId == paradeId).ToListAsync(cancellationToken);
        var userIds = scored.Select(s => s.UserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        return [.. scored
            .Where(s => entries.TryGetValue(s.RegistrationId, out var e) && !assignments.Any(a => a.UserId == s.UserId && a.CategoryId == e.CategoryId))
            .Select(s =>
            {
                var e = entries[s.RegistrationId];
                return new OutsideScore(s.UserId, names.GetValueOrDefault(s.UserId, ""), s.RegistrationId, e.StartNumber, e.GroupName, e.CategoryName, s.Passes,
                    reviews.SingleOrDefault(r => r.UserId == s.UserId && r.RegistrationId == s.RegistrationId)?.Decision);
            })
            .OrderBy(o => o.JurorName).ThenBy(o => o.StartNumber ?? int.MaxValue)];
    }

    /// <summary>Akkoord of afwijzen (of weer open zetten met <c>null</c>) per jurylid en inzending.</summary>
    public async Task DecideOutsideAsync(Guid paradeId, IReadOnlyList<OutsideDecisionInput> decisions, CancellationToken cancellationToken)
    {
        var outside = (await OutsideAsync(paradeId, cancellationToken)).Select(o => (o.UserId, o.RegistrationId)).ToHashSet();
        if (decisions.Any(d => !outside.Contains((d.UserId, d.RegistrationId))))
        {
            throw new DomainException(ErrorCodes.Validation, "Deze beoordeling valt niet buiten de categorieën van het jurylid.");
        }

        var existing = await db.JudgingOutsideReviews.Where(r => r.ParadeId == paradeId).ToListAsync(cancellationToken);
        foreach (var d in decisions)
        {
            var review = existing.SingleOrDefault(r => r.UserId == d.UserId && r.RegistrationId == d.RegistrationId);
            if (d.Decision is not { } decision)
            {
                if (review is not null)
                {
                    db.JudgingOutsideReviews.Remove(review);
                }

                continue;
            }

            if (review is null)
            {
                db.JudgingOutsideReviews.Add(new JudgingOutsideReview
                {
                    ParadeId = paradeId,
                    UserId = d.UserId,
                    RegistrationId = d.RegistrationId,
                    Decision = decision,
                    DecidedBy = actor.UserId,
                    DecidedAt = Now,
                });
            }
            else
            {
                (review.Decision, review.DecidedBy, review.DecidedAt) = (decision, actor.UserId, Now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("jury.outside-decided", "Parade", paradeId.ToString(), null, JsonSerializer.Serialize(decisions, Json)), cancellationToken);
    }
}
