using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Parade.Groups;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.ParadeManagement;

/// <summary>De gegevens van een groep zelf; de jaren gaan apart.</summary>
public sealed record CarnivalGroupInput(
    string Name, string? ContactName, string? ContactPhone, string? ContactEmail, int? ParticipatingSince, string? Notes, bool Active);

/// <summary>Wat je per jaar zelf invult; leeg laten betekent "neem over van de inschrijving".</summary>
public sealed record CarnivalGroupSeasonInput(string? FloatName, int? CategoryId, int? Place, string? Notes);

/// <summary>De inschrijving van dat jaar, zoals de groep hem heeft ingediend.</summary>
public sealed record GroupRegistrationView(
    Guid Id, int? RegistrationNumber, int? StartNumber, RegistrationStatus Status, string? GroupName, string? ContactName,
    string? ContactPhone, string? ContactEmail, int? CategoryId, string? Subject, string? SubjectDescription,
    int ChildrenCount, int AdultCount, bool? HasMusic, string? BuildAddress, string? JuryAddress,
    decimal? EstimatedLengthMeters, decimal? MeasuredLengthMeters, DateTime? SubmittedAt);

/// <summary>Een jaar van een groep: eigen antwoorden, aangevuld met de inschrijving van dat jaar.</summary>
public sealed record CarnivalGroupYear(
    int Year, string? FloatName, int? CategoryId, string? CategoryName, int? Place, bool PlaceFromResults, string? Notes,
    bool HasOwnRow, GroupRegistrationView? Registration);

public sealed record CarnivalGroupSummary(
    Guid Id, string Name, bool Active, int? ParticipatingSince, int? FirstYear, int YearCount, int? LastYear,
    string? LastFloatName, int? LastPlace, bool RegisteredForCurrentParade);

public sealed record CarnivalGroupDetail(
    Guid Id, string Name, bool Active, string? ContactName, string? ContactPhone, string? ContactEmail,
    int? ParticipatingSince, string? Notes, int? FirstYear, IReadOnlyList<CarnivalGroupYear> Years);

/// <summary>Categorie om bij een jaar te kiezen (ook de niet-actieve, want oude jaren horen soms bij een oude categorie).</summary>
public sealed record ParadeCategoryOption(int Id, string Name, bool Active);

/// <summary>Een ingediende inschrijving die nog aan geen groep hangt, met de groep die er het meest op lijkt.</summary>
public sealed record UnlinkedRegistration(
    Guid Id, int? RegistrationNumber, string? GroupName, string? ContactName, string? ContactEmail, int Year,
    RegistrationStatus Status, Guid? SuggestedGroupId, string? SuggestedGroupName);

/// <summary>
/// Het register van carnavalsgroepen (wens 2026-11-17). Een groep blijft over de jaren bestaan; per jaar houden we bij
/// hoe de wagen heette, in welke categorie ze meededen en op welke plaats ze eindigden. Zodra een groep zich
/// inschrijft hangt die inschrijving aan de groep, en daarmee staan het adres, de contactpersoon en de aantallen bij
/// de groep — zonder ze te kopiëren.
///
/// Wat je zelf invult gaat voor: een leeg veld in een jaarrij betekent "neem over van de inschrijving", een gevuld
/// veld blijft staan. De plaats komt automatisch uit de uitslag als die gepubliceerd wordt, en kan voor de oude jaren
/// met de hand ingevuld worden.
/// </summary>
public sealed class CarnivalGroups(DrammersDbContext db, IAuditLogger audit, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CarnivalGroupSummary>> ListAsync(string? search, bool includeInactive, CancellationToken cancellationToken)
    {
        var groups = db.CarnivalGroups.AsNoTracking();
        if (!includeInactive)
        {
            groups = groups.Where(g => g.Active);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var match = CarnivalGroup.Normalize(search);
            groups = groups.Where(g => g.MatchName.Contains(match));
        }

        var list = await groups.OrderBy(g => g.Name)
            .Select(g => new { g.Id, g.Name, g.Active, g.ParticipatingSince })
            .ToListAsync(cancellationToken);
        var ids = list.Select(g => g.Id).ToList();
        var seasons = await db.CarnivalGroupSeasons.AsNoTracking().Where(s => ids.Contains(s.GroupId))
            .Select(s => new { s.GroupId, s.Year, s.FloatName, s.Place }).ToListAsync(cancellationToken);
        var registrations = await db.ParadeRegistrations.AsNoTracking()
            .Where(r => r.CarnivalGroupId != null && ids.Contains(r.CarnivalGroupId.Value) && ParadeRegistration.Active.Contains(r.Status))
            .Join(db.Parades.AsNoTracking(), r => r.ParadeId, p => p.Id, (r, p) => new { GroupId = r.CarnivalGroupId!.Value, Year = p.ParadeDate.Year, r.Subject })
            .ToListAsync(cancellationToken);
        var currentParade = await db.CurrentParades().AsNoTracking().Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        var currentGroups = currentParade is null
            ? []
            : (await db.ParadeRegistrations.AsNoTracking()
                .Where(r => r.ParadeId == currentParade && r.CarnivalGroupId != null && ParadeRegistration.Active.Contains(r.Status))
                .Select(r => r.CarnivalGroupId!.Value).ToListAsync(cancellationToken)).ToHashSet();

        return
        [
            .. list.Select(g =>
            {
                var years = seasons.Where(s => s.GroupId == g.Id).Select(s => s.Year)
                    .Concat(registrations.Where(r => r.GroupId == g.Id).Select(r => r.Year)).Distinct().OrderBy(y => y).ToList();
                var last = years.Count > 0 ? years[^1] : (int?)null;
                var lastSeason = seasons.FirstOrDefault(s => s.GroupId == g.Id && s.Year == last);
                var lastSubject = registrations.FirstOrDefault(r => r.GroupId == g.Id && r.Year == last)?.Subject;
                return new CarnivalGroupSummary(g.Id, g.Name, g.Active, g.ParticipatingSince, years.Count > 0 ? years[0] : null,
                    years.Count, last, lastSeason?.FloatName ?? lastSubject, lastSeason?.Place, currentGroups.Contains(g.Id));
            }),
        ];
    }

    /// <summary>De categorieën om bij een jaar te kiezen; met <c>parade.read</c>, zodat deze pagina los staat.</summary>
    public async Task<IReadOnlyList<ParadeCategoryOption>> CategoriesAsync(CancellationToken cancellationToken) =>
        await db.ParadeCategories.AsNoTracking().Where(c => c.ParadeId == null).OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ParadeCategoryOption(c.Id, c.Name, c.Active)).ToListAsync(cancellationToken);

    public async Task<CarnivalGroupDetail> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await db.CarnivalGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id, cancellationToken) ?? throw NotFound();
        var seasons = await db.CarnivalGroupSeasons.AsNoTracking().Where(s => s.GroupId == id).ToListAsync(cancellationToken);
        var registrations = await db.ParadeRegistrations.AsNoTracking().Where(r => r.CarnivalGroupId == id)
            .Join(db.Parades.AsNoTracking(), r => r.ParadeId, p => p.Id, (r, p) => new { Registration = r, Year = p.ParadeDate.Year })
            .ToListAsync(cancellationToken);
        var categoryIds = seasons.Where(s => s.CategoryId is not null).Select(s => s.CategoryId!.Value)
            .Concat(registrations.Where(r => r.Registration.CategoryId is not null).Select(r => r.Registration.CategoryId!.Value)).Distinct().ToList();
        var categories = await db.ParadeCategories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var years = new List<CarnivalGroupYear>();
        foreach (var year in seasons.Select(s => s.Year).Concat(registrations.Select(r => r.Year)).Distinct().OrderByDescending(y => y))
        {
            var season = seasons.FirstOrDefault(s => s.Year == year);
            // Een groep kan in één jaar maar één keer meedoen; bij meerdere (bijv. een ingetrokken inschrijving) de laatste.
            var registration = registrations.Where(r => r.Year == year)
                .OrderByDescending(r => ParadeRegistration.Active.Contains(r.Registration.Status)).ThenByDescending(r => r.Registration.CreatedAt)
                .Select(r => r.Registration).FirstOrDefault();
            var categoryId = season?.CategoryId ?? registration?.CategoryId;
            years.Add(new CarnivalGroupYear(year, season?.FloatName ?? registration?.Subject, categoryId,
                categoryId is { } c && categories.TryGetValue(c, out var name) ? name : null,
                season?.Place, season?.PlaceFromResultsAt is not null, season?.Notes, season is not null,
                registration is null ? null : View(registration)));
        }

        return new CarnivalGroupDetail(group.Id, group.Name, group.Active, group.ContactName, group.ContactPhone, group.ContactEmail,
            group.ParticipatingSince, group.Notes, years.Count > 0 ? years[^1].Year : null, years);
    }

    public async Task<Guid> CreateAsync(CarnivalGroupInput input, CancellationToken cancellationToken)
    {
        var group = new CarnivalGroup { Id = IdGenerator.NewId(), Name = input.Name.Trim(), MatchName = Match(input.Name) };
        Apply(group, input);
        await EnsureUniqueAsync(group.MatchName, null, cancellationToken);
        db.CarnivalGroups.Add(group);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("carnival-group.created", "CarnivalGroup", group.Id.ToString(), null,
            JsonSerializer.Serialize(new { group.Name }, Json)), cancellationToken);
        return group.Id;
    }

    public async Task UpdateAsync(Guid id, CarnivalGroupInput input, CancellationToken cancellationToken)
    {
        var group = await db.CarnivalGroups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken) ?? throw NotFound();
        var match = Match(input.Name);
        await EnsureUniqueAsync(match, id, cancellationToken);
        (group.Name, group.MatchName) = (input.Name.Trim(), match);
        Apply(group, input);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("carnival-group.updated", "CarnivalGroup", id.ToString(), null,
            JsonSerializer.Serialize(new { group.Name, group.Active }, Json)), cancellationToken);
    }

    /// <summary>
    /// Verwijderen kan alleen zolang er geen inschrijving aan de groep hangt; daarna zou de geschiedenis van de
    /// optocht losraken. Een groep die gestopt is, zet je op niet actief.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await db.CarnivalGroups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken) ?? throw NotFound();
        if (await db.ParadeRegistrations.AnyAsync(r => r.CarnivalGroupId == id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.CarnivalGroupInUse,
                "Aan deze groep hangen inschrijvingen. Zet de groep op niet actief in plaats van verwijderen.", DomainErrorKind.Conflict);
        }

        db.CarnivalGroups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("carnival-group.deleted", "CarnivalGroup", id.ToString(), null,
            JsonSerializer.Serialize(new { group.Name }, Json)), cancellationToken);
    }

    /// <summary>Een jaar van een groep opslaan; ook voor een jaar van vóór de app, dus zonder inschrijving.</summary>
    public async Task SaveYearAsync(Guid groupId, int year, CarnivalGroupSeasonInput input, CancellationToken cancellationToken)
    {
        if (!await db.CarnivalGroups.AnyAsync(g => g.Id == groupId, cancellationToken))
        {
            throw NotFound();
        }

        if (year is < 1900 or > 2200)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een jaar in tussen 1900 en 2200.");
        }

        if (input.CategoryId is { } categoryId && !await db.ParadeCategories.AnyAsync(c => c.Id == categoryId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.CategoryNotFound, "Categorie niet gevonden.", DomainErrorKind.NotFound);
        }

        var season = await db.CarnivalGroupSeasons.SingleOrDefaultAsync(s => s.GroupId == groupId && s.Year == year, cancellationToken);
        if (season is null)
        {
            season = new CarnivalGroupSeason { GroupId = groupId, Year = year };
            db.CarnivalGroupSeasons.Add(season);
        }

        season.FloatName = Trim(input.FloatName);
        season.CategoryId = input.CategoryId;
        season.Notes = Trim(input.Notes);
        if (season.Place != input.Place)
        {
            // Met de hand gewijzigd: dan is het niet meer de plaats uit de uitslag.
            (season.Place, season.PlaceFromResultsAt) = (input.Place, null);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("carnival-group.year-saved", "CarnivalGroup", groupId.ToString(), null,
            JsonSerializer.Serialize(new { year, input.Place }, Json)), cancellationToken);
    }

    public async Task DeleteYearAsync(Guid groupId, int year, CancellationToken cancellationToken)
    {
        var season = await db.CarnivalGroupSeasons.SingleOrDefaultAsync(s => s.GroupId == groupId && s.Year == year, cancellationToken)
            ?? throw new DomainException(ErrorCodes.SeasonNotFound, "Dit jaar staat niet bij de groep.", DomainErrorKind.NotFound);
        db.CarnivalGroupSeasons.Remove(season);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("carnival-group.year-deleted", "CarnivalGroup", groupId.ToString(), null,
            JsonSerializer.Serialize(new { year }, Json)), cancellationToken);
    }

    /// <summary>Ingediende inschrijvingen die nog niet aan een groep hangen, met een voorstel op naam.</summary>
    public async Task<IReadOnlyList<UnlinkedRegistration>> UnlinkedAsync(CancellationToken cancellationToken)
    {
        var open = await db.ParadeRegistrations.AsNoTracking()
            .Where(r => r.CarnivalGroupId == null && ParadeRegistration.Active.Contains(r.Status))
            .Join(db.Parades.AsNoTracking(), r => r.ParadeId, p => p.Id, (r, p) => new
            {
                r.Id,
                r.RegistrationNumber,
                r.GroupName,
                r.ContactName,
                r.ContactEmail,
                r.Status,
                Year = p.ParadeDate.Year,
            })
            .OrderByDescending(r => r.Year).ThenBy(r => r.RegistrationNumber).ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return [];
        }

        var groups = await db.CarnivalGroups.AsNoTracking().Where(g => g.Active).Select(g => new { g.Id, g.Name, g.MatchName })
            .ToListAsync(cancellationToken);
        return
        [
            .. open.Select(r =>
            {
                var match = r.GroupName is null ? "" : CarnivalGroup.Normalize(r.GroupName);
                // Eerst precies dezelfde naam (die komt voor als de groep ná het indienen is aangemaakt), anders een
                // naam die in de andere zit: "kuitenbijters" tegenover "cv de kuitenbijters".
                var suggestion = match.Length == 0
                    ? null
                    : groups.FirstOrDefault(g => g.MatchName == match)
                        ?? groups.FirstOrDefault(g => g.MatchName.Contains(match) || match.Contains(g.MatchName));
                return new UnlinkedRegistration(r.Id, r.RegistrationNumber, r.GroupName, r.ContactName, r.ContactEmail, r.Year, r.Status,
                    suggestion?.Id, suggestion?.Name);
            }),
        ];
    }

    /// <summary>
    /// Een inschrijving aan een groep hangen: aan een bestaande groep (<paramref name="groupId"/>) of aan een nieuwe
    /// groep met de naam uit de inschrijving. Bestaat die naam al, dan wordt de bestaande groep gebruikt.
    /// </summary>
    public async Task<Guid> LinkAsync(Guid registrationId, Guid? groupId, bool createNew, CancellationToken cancellationToken)
    {
        var registration = await db.ParadeRegistrations.SingleOrDefaultAsync(r => r.Id == registrationId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.RegistrationNotFound, "Inschrijving niet gevonden.", DomainErrorKind.NotFound);
        Guid target;
        if (groupId is { } id)
        {
            target = await db.CarnivalGroups.AnyAsync(g => g.Id == id, cancellationToken) ? id : throw NotFound();
        }
        else if (createNew)
        {
            if (string.IsNullOrWhiteSpace(registration.GroupName))
            {
                throw new DomainException(ErrorCodes.Validation, "Deze inschrijving heeft geen groepsnaam; kies een bestaande groep.");
            }

            var match = Match(registration.GroupName);
            target = await db.CarnivalGroups.Where(g => g.MatchName == match).Select(g => g.Id).FirstOrDefaultAsync(cancellationToken);
            if (target == default)
            {
                target = await CreateAsync(new CarnivalGroupInput(registration.GroupName, registration.ContactName, registration.ContactPhone,
                    registration.ContactEmail, null, null, true), cancellationToken);
            }
        }
        else
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een groep of laat er een aanmaken.");
        }

        registration.CarnivalGroupId = target;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("carnival-group.linked", "ParadeRegistration", registrationId.ToString(), null,
            JsonSerializer.Serialize(new { groupId = target }, Json)), cancellationToken);
        return target;
    }

    public async Task UnlinkAsync(Guid registrationId, CancellationToken cancellationToken)
    {
        var registration = await db.ParadeRegistrations.SingleOrDefaultAsync(r => r.Id == registrationId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.RegistrationNotFound, "Inschrijving niet gevonden.", DomainErrorKind.NotFound);
        registration.CarnivalGroupId = null;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("carnival-group.unlinked", "ParadeRegistration", registrationId.ToString(), null, null), cancellationToken);
    }

    /// <summary>
    /// Bij indienen zelf koppelen als de naam exact overeenkomt met een actieve groep. Alleen de koppeling wordt
    /// gezet; de aanroeper slaat op. Komt de naam niet overeen, dan blijft de inschrijving open staan in het portal.
    /// </summary>
    public async Task LinkOnSubmitAsync(ParadeRegistration registration, CancellationToken cancellationToken)
    {
        if (registration.CarnivalGroupId is not null || string.IsNullOrWhiteSpace(registration.GroupName))
        {
            return;
        }

        var match = CarnivalGroup.Normalize(registration.GroupName);
        var group = await db.CarnivalGroups.Where(g => g.Active && g.MatchName == match).Select(g => g.Id).FirstOrDefaultAsync(cancellationToken);
        if (group != default)
        {
            registration.CarnivalGroupId = group;
        }
    }

    /// <summary>
    /// De plaatsen uit een gepubliceerde uitslag bij de groepen zetten (per categorie de plaats van de inzending).
    /// Een plaats die met de hand is ingevuld blijft staan: die weet het beter dan wij.
    /// </summary>
    public async Task SavePlacesAsync(Guid paradeId, int year, IReadOnlyCollection<(Guid RegistrationId, int Place)> places, CancellationToken cancellationToken)
    {
        if (places.Count == 0)
        {
            return;
        }

        var ids = places.Select(p => p.RegistrationId).ToList();
        var groups = await db.ParadeRegistrations.AsNoTracking()
            .Where(r => ids.Contains(r.Id) && r.CarnivalGroupId != null)
            .Select(r => new { r.Id, GroupId = r.CarnivalGroupId!.Value, r.CategoryId, r.Subject }).ToListAsync(cancellationToken);
        if (groups.Count == 0)
        {
            return;
        }

        var groupIds = groups.Select(g => g.GroupId).ToList();
        var seasons = await db.CarnivalGroupSeasons.Where(s => groupIds.Contains(s.GroupId) && s.Year == year).ToListAsync(cancellationToken);
        var now = clock.UtcNow.UtcDateTime;
        var written = 0;
        foreach (var entry in groups)
        {
            var place = places.First(p => p.RegistrationId == entry.Id).Place;
            var season = seasons.FirstOrDefault(s => s.GroupId == entry.GroupId);
            if (season is null)
            {
                season = new CarnivalGroupSeason { GroupId = entry.GroupId, Year = year };
                db.CarnivalGroupSeasons.Add(season);
                seasons.Add(season);
            }
            else if (season.Place is not null && season.PlaceFromResultsAt is null)
            {
                continue;
            }

            (season.Place, season.PlaceFromResultsAt) = (place, now);
            written++;
        }

        if (written > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(new AuditEntry("carnival-group.places-saved", "Parade", paradeId.ToString(), null,
                JsonSerializer.Serialize(new { year, count = written }, Json)), cancellationToken);
        }
    }

    private static GroupRegistrationView View(ParadeRegistration r) => new(
        r.Id, r.RegistrationNumber, r.StartNumber, r.Status, r.GroupName, r.ContactName, r.ContactPhone, r.ContactEmail, r.CategoryId,
        r.Subject, r.SubjectDescription, r.ChildrenCount, r.AdultCount, r.HasMusic, Line(r.BuildAddress), Line(r.EffectiveJuryAddress),
        r.EstimatedLengthMeters, r.MeasuredLengthMeters, r.SubmittedAt);

    /// <summary>Adres op één regel, zoals het portal het laat zien; leeg als er niets is ingevuld.</summary>
    private static string? Line(Address a)
    {
        if (a.IsEmpty)
        {
            return null;
        }

        var street = string.Join(' ', new[] { a.Street, a.HouseNumber, a.Addition }.Where(p => !string.IsNullOrWhiteSpace(p)));
        var city = string.Join(' ', new[] { a.PostalCode, a.City }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.Join(", ", new[] { street, city }.Where(p => p.Length > 0));
    }

    private static void Apply(CarnivalGroup group, CarnivalGroupInput input)
    {
        group.ContactName = Trim(input.ContactName);
        group.ContactPhone = Trim(input.ContactPhone);
        group.ContactEmail = Trim(input.ContactEmail);
        group.ParticipatingSince = input.ParticipatingSince;
        group.Notes = Trim(input.Notes);
        group.Active = input.Active;
    }

    private async Task EnsureUniqueAsync(string match, Guid? exclude, CancellationToken cancellationToken)
    {
        if (await db.CarnivalGroups.AnyAsync(g => g.MatchName == match && (exclude == null || g.Id != exclude), cancellationToken))
        {
            throw new DomainException(ErrorCodes.GroupNameTaken, "Er is al een groep met deze naam.", DomainErrorKind.Conflict);
        }
    }

    private static string Match(string name)
    {
        var match = CarnivalGroup.Normalize(name);
        return match.Length > 0 ? match : throw new DomainException(ErrorCodes.Validation, "Vul een naam in.");
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DomainException NotFound() => new(ErrorCodes.GroupNotFound, "Groep niet gevonden.", DomainErrorKind.NotFound);
}
