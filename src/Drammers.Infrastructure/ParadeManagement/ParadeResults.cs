using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Parade.Judging;
using Drammers.Modules.Parade.Parades;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.ParadeManagement;

/// <summary>Instellingen <c>Results:*</c>: wie een mail krijgt als de uitslag is gepubliceerd.</summary>
public sealed class ResultsOptions
{
    public const string SectionName = "Results";

    public List<string> NotifyAddresses { get; set; } = ["secretaris@vrolijkedrammers.nl", "voorzitter@vrolijkedrammers.nl"];
}

/// <summary>Een plaats in de uitslag: punten per criterium (al gewogen) en het totaal, afgerond op één decimaal.</summary>
public sealed record ResultRow(
    int Place, Guid RegistrationId, int? StartNumber, string GroupName, string? Motto,
    decimal Originality, decimal Carnivalesque, decimal Quality, decimal Overall, decimal Total, ResultPhoto? Photo = null, int PhotoCount = 0);

/// <summary>De eerste (zichtbare, verwerkte) foto van een inzending.</summary>
public sealed record ResultPhoto(Guid Id, string? ThumbnailBlobPath, string? DisplayBlobPath);

/// <summary>De uitslag van een categorie; <see cref="Rows"/> is leeg zolang niet alle juryleden hebben ingediend.</summary>
public sealed record CategoryResult(
    int CategoryId, string Name, int Jurors, int Submitted, bool Ready, int WeightOriginality, int WeightCarnivalesque,
    int WeightQuality, int WeightOverall, int MaxPoints, int Entries, IReadOnlyList<ResultRow> Rows);

public sealed record ParadeResultOverview(
    Guid ParadeId, string ParadeName, DateOnly ParadeDate, DateTime? PublishedAt, IReadOnlyList<CategoryResult> Categories, Guid? AlbumId = null);

/// <summary>
/// Uitslag van de optocht (fase 22c). Per jurylid en criterium telt het gemiddelde van de ingevulde passages; per
/// criterium de som over de juryleden, maal de weging van de categorie; het totaal is de som van de vier criteria.
/// Mee tellen: de scores van juryleden die hebben ingediend, in hun eigen categorieën, plus beoordelingen buiten
/// categorie waar het bestuur of de hoofdjury akkoord op gaf. Een categorie is klaar als al haar juryleden hebben
/// ingediend. Alleen de uitslagcommissie ziet de uitslag; publiceren kan pas na de prijsuitreiking.
/// </summary>
public sealed class ParadeResults(
    DrammersDbContext db,
    ParadeJudging judging,
    Content.ContentAdministration content,
    IEmailSender email,
    IOptions<ResultsOptions> options,
    IAuditLogger audit,
    ICurrentActor actor,
    IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ParadeResultOverview> OverviewAsync(Guid? paradeId, CancellationToken cancellationToken)
    {
        var parade = await (paradeId is { } id ? db.Parades.Where(p => p.Id == id) : db.CurrentParades()).AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Er is (nog) geen optocht.", DomainErrorKind.NotFound);
        return new ParadeResultOverview(parade.Id, parade.Name, parade.ParadeDate, parade.ResultsPublishedAt, await CalculateAsync(parade.Id, cancellationToken),
            parade.ResultsAlbumId);
    }

    /// <summary>De gepubliceerde uitslag (website en app): de laatst gepubliceerde optocht, of <c>null</c>.</summary>
    public async Task<ParadeResultOverview?> PublishedAsync(CancellationToken cancellationToken)
    {
        var parade = await db.Parades.AsNoTracking().Where(p => p.ResultsPublishedAt != null)
            .OrderByDescending(p => p.ParadeDate).ThenByDescending(p => p.ResultsPublishedAt).FirstOrDefaultAsync(cancellationToken);
        return parade is null
            ? null
            : new ParadeResultOverview(parade.Id, parade.Name, parade.ParadeDate, parade.ResultsPublishedAt,
                [.. (await CalculateAsync(parade.Id, cancellationToken)).Where(c => c.Ready && c.Rows.Count > 0)], parade.ResultsAlbumId);
    }

    private async Task<IReadOnlyList<CategoryResult>> CalculateAsync(Guid paradeId, CancellationToken cancellationToken)
    {
        var entries = await judging.EntriesAsync(paradeId, cancellationToken);
        var categories = await (
            from j in db.ParadeJudgingCategories.AsNoTracking().Where(j => j.ParadeId == paradeId && j.Judged)
            join c in db.ParadeCategories.AsNoTracking() on j.CategoryId equals c.Id
            orderby c.SortOrder, c.Name
            select new { j, c.Name }).ToListAsync(cancellationToken);
        var assignments = await db.ParadeJurorAssignments.AsNoTracking().Where(a => a.ParadeId == paradeId).ToListAsync(cancellationToken);
        var submitted = (await db.JudgingSubmissions.AsNoTracking().Where(s => s.ParadeId == paradeId).Select(s => s.UserId).ToListAsync(cancellationToken)).ToHashSet();
        var approved = (await db.JudgingOutsideReviews.AsNoTracking()
            .Where(r => r.ParadeId == paradeId && r.Decision == OutsideDecision.Approved)
            .Select(r => new { r.UserId, r.RegistrationId }).ToListAsync(cancellationToken)).Select(r => (r.UserId, r.RegistrationId)).ToHashSet();
        var scores = await db.JudgingScores.AsNoTracking().Where(s => s.ParadeId == paradeId && submitted.Contains(s.UserId)).ToListAsync(cancellationToken);
        var entryIds = entries.Select(e => e.RegistrationId).ToList();
        var photos = await db.Photos.AsNoTracking()
            .Where(f => f.RegistrationId != null && entryIds.Contains(f.RegistrationId.Value) && !f.Hidden
                && f.ProcessingStatus == Modules.Content.Photos.PhotoProcessingStatus.Ready)
            .OrderBy(f => f.SortOrder).Select(f => new { RegistrationId = f.RegistrationId!.Value, f.Id, f.ThumbnailBlobPath, f.DisplayBlobPath })
            .ToListAsync(cancellationToken);

        var result = new List<CategoryResult>();
        foreach (var category in categories.Where(c => entries.Any(e => e.CategoryId == c.j.CategoryId)))
        {
            var w = category.j;
            var jurors = assignments.Where(a => a.CategoryId == w.CategoryId).Select(a => a.UserId).Distinct().ToList();
            var ready = jurors.Count > 0 && jurors.All(submitted.Contains);
            var categoryEntries = entries.Where(e => e.CategoryId == w.CategoryId).ToList();
            var rows = new List<(JudgingEntry Entry, decimal[] Points)>();
            if (ready)
            {
                foreach (var entry in categoryEntries)
                {
                    var counted = scores.Where(s => s.RegistrationId == entry.RegistrationId
                        && (jurors.Contains(s.UserId) || approved.Contains((s.UserId, s.RegistrationId))));
                    var points = Enum.GetValues<JudgingCriterion>().Select(criterion => counted
                        .Where(s => s.Criterion == criterion)
                        .GroupBy(s => s.UserId)
                        .Sum(g => (decimal)g.Average(s => s.Value)) * w.WeightOf(criterion)).ToArray();
                    rows.Add((entry, points));
                }
            }

            var ordered = rows.Select(r => (r.Entry, r.Points, Total: Math.Round(r.Points.Sum(), 1))).OrderByDescending(r => r.Total).ThenBy(r => r.Entry.StartNumber ?? int.MaxValue).ToList();
            var ranked = ordered.Select((r, i) => new ResultRow(
                ordered.FindIndex(o => o.Total == r.Total) + 1, r.Entry.RegistrationId, r.Entry.StartNumber, r.Entry.GroupName, r.Entry.Motto,
                Math.Round(r.Points[0], 1), Math.Round(r.Points[1], 1), Math.Round(r.Points[2], 1), Math.Round(r.Points[3], 1), r.Total,
                photos.Where(f => f.RegistrationId == r.Entry.RegistrationId).Select(f => new ResultPhoto(f.Id, f.ThumbnailBlobPath, f.DisplayBlobPath)).FirstOrDefault(),
                photos.Count(f => f.RegistrationId == r.Entry.RegistrationId))).ToList();
            var weights = w.WeightOriginality + w.WeightCarnivalesque + w.WeightQuality + w.WeightOverall;
            result.Add(new CategoryResult(w.CategoryId, category.Name, jurors.Count, jurors.Count(submitted.Contains), ready,
                w.WeightOriginality, w.WeightCarnivalesque, w.WeightQuality, w.WeightOverall, jurors.Count * 100 * weights, categoryEntries.Count, ranked));
        }

        return result;
    }

    /// <summary>
    /// Publiceren, alleen na de prijsuitreiking (<paramref name="prizeCeremonyHeld"/>) en als alle categorieën klaar zijn.
    /// De optocht is daarna afgerond; de secretaris en de voorzitter krijgen een mail.
    /// </summary>
    public async Task<DateTime> PublishAsync(Guid paradeId, bool prizeCeremonyHeld, CancellationToken cancellationToken)
    {
        if (!prizeCeremonyHeld)
        {
            throw new DomainException(ErrorCodes.Validation, "Publiceer de uitslag pas na de prijsuitreiking.");
        }

        var parade = await db.Parades.SingleOrDefaultAsync(p => p.Id == paradeId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Optocht niet gevonden.", DomainErrorKind.NotFound);
        if (parade.ResultsPublishedAt is { } already)
        {
            return already;
        }

        var notReady = (await CalculateAsync(paradeId, cancellationToken)).Where(c => !c.Ready).Select(c => c.Name).ToList();
        if (notReady.Count > 0)
        {
            throw new DomainException(ErrorCodes.ResultsNotReady,
                $"Nog niet alle juryleden hebben ingediend voor: {string.Join(", ", notReady)}.", DomainErrorKind.Conflict);
        }

        var now = clock.UtcNow.UtcDateTime;
        (parade.ResultsPublishedAt, parade.ResultsPublishedBy, parade.Status) = (now, actor.UserId, ParadeStatus.Completed);
        // Fase 22d: het album met foto's van de inzendingen gaat tegelijk met de uitslag open.
        if (parade.ResultsAlbumId is { } albumId && await db.PhotoAlbums.SingleOrDefaultAsync(a => a.Id == albumId, cancellationToken) is { } album)
        {
            (album.Status, album.PublishAt) = (Modules.Content.Shared.PublicationStatus.Published, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade.results-published", "Parade", paradeId.ToString(), null, JsonSerializer.Serialize(new { prizeCeremonyHeld }, Json)), cancellationToken);

        var who = actor.UserId is { } userId ? await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken) : null;
        foreach (var address in options.Value.NotifyAddresses.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await email.SendAsync(PublishedMail(address, parade.Name, who, now), cancellationToken);
        }

        return now;
    }

    /// <summary>
    /// Foto's bij een inzending (fase 22d), ook achteraf. Ze komen in het album "Uitslag …" van de optocht (soort Optocht,
    /// voor iedereen), dat pas samen met de uitslag zichtbaar wordt; daarna meteen.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> AddPhotosAsync(Guid registrationId, IReadOnlyList<Content.UploadedFile> uploads, CancellationToken cancellationToken)
    {
        var entry = await db.ParadeRegistrations.AsNoTracking().Where(r => r.Id == registrationId).Select(r => new { r.ParadeId, r.GroupName }).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Inzending niet gevonden.", DomainErrorKind.NotFound);
        var parade = await db.Parades.SingleAsync(p => p.Id == entry.ParadeId, cancellationToken);
        if (parade.ResultsAlbumId is not { } albumId || !await db.PhotoAlbums.AnyAsync(a => a.Id == albumId, cancellationToken))
        {
            albumId = await content.CreateAlbumAsync(new Content.AlbumInput(
                $"Uitslag {parade.Name}", parade.ParadeDate, $"Foto's van de inzendingen bij de uitslag van {parade.Name}.", null,
                new Content.PublicationInput(Modules.Content.Shared.ContentVisibility.Public, [],
                    parade.ResultsPublishedAt is null ? Modules.Content.Shared.PublicationStatus.Draft : Modules.Content.Shared.PublicationStatus.Published, null),
                Modules.Content.Photos.PhotoCategory.Parade), cancellationToken);
            parade.ResultsAlbumId = albumId;
            await db.SaveChangesAsync(cancellationToken);
        }

        var ids = await content.UploadPhotosAsync(albumId, uploads, cancellationToken);
        await db.Photos.Where(f => ids.Contains(f.Id)).ExecuteUpdateAsync(u => u
            .SetProperty(f => f.RegistrationId, registrationId)
            .SetProperty(f => f.Caption, f => f.Caption ?? entry.GroupName), cancellationToken);
        return ids;
    }

    public static EmailMessage PublishedMail(string to, string paradeName, string? publishedBy, DateTime at)
    {
        var when = TimeZoneInfo.ConvertTimeFromUtc(at, TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"))
            .ToString("dddd d MMMM yyyy 'om' HH:mm", System.Globalization.CultureInfo.GetCultureInfo("nl-NL"));
        var by = publishedBy is null ? "" : $" door {publishedBy}";
        var subject = $"Uitslag {paradeName} is gepubliceerd";
        var text = $"""
            Hallo,

            De uitslag van {paradeName} is gepubliceerd{by} op {when}. Hij staat nu op de website en in de app.

            Groeten,
            De app van De Vrolijke Drammers
            """;
        var html = $"<p>Hallo,</p><p>De uitslag van {System.Net.WebUtility.HtmlEncode(paradeName)} is gepubliceerd{System.Net.WebUtility.HtmlEncode(by)} op {when}. Hij staat nu op de website en in de app.</p><p>Groeten,<br>De app van De Vrolijke Drammers</p>";
        return new EmailMessage(to, subject, text, html);
    }
}
