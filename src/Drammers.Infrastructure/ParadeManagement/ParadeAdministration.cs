using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Parade.Categories;
using Drammers.Modules.Parade.Parades;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.ParadeManagement;

public sealed record ParadeInput(
    int CarnivalYearId,
    string Name,
    DateOnly ParadeDate,
    TimeOnly StartTime,
    string? StartLocation,
    string? RouteDescription,
    decimal? RouteLengthKm,
    DateTime RegistrationOpensAt,
    DateTime RegistrationClosesAt,
    DateTime? EditDeadlineAt,
    bool SubjectRequired,
    decimal DefaultSpacingMeters,
    int MaxDocumentsPerRegistration,
    int MaxDocumentSizeMb,
    ParadeStatus Status,
    string? InfoText = null);

public sealed record CategoryInput(
    string Code,
    string Name,
    AgeGroup AgeGroup,
    CategoryType Type,
    int? MinimumParticipants,
    int? MaximumParticipants,
    ParticipantCountBasis ParticipantCountBasis,
    ValidationMode ValidationMode,
    bool HasVehicle,
    bool Active,
    int SortOrder);

/// <summary>
/// Optocht en categorieën configureren (fase 11, <c>parade.config</c>). Formeel één optocht per carnavalsjaar, maar er
/// kunnen er meer zijn (fase 22a); bij het aanmaken
/// ontstaat de teller voor opgavenummers (ADR-011). Categorieën worden niet verwijderd maar gedeactiveerd, zodat
/// inschrijvingen hun categorie houden.
/// </summary>
public sealed class ParadeAdministration(DrammersDbContext db, IAuditLogger audit, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>De huidige optocht (zie <see cref="CurrentParade"/>), of <c>null</c>.</summary>
    public async Task<Parade?> CurrentAsync(CancellationToken cancellationToken) =>
        await db.CurrentParades().AsNoTracking().FirstOrDefaultAsync(cancellationToken);

    public async Task<Parade> CreateAsync(ParadeInput input, CancellationToken cancellationToken)
    {
        await ValidateAsync(input, null, cancellationToken);
        var parade = new Parade { Id = IdGenerator.NewId(), Name = input.Name.Trim(), FixedEntries = ParadeFixedEntry.Defaults(), ArrivalLocation = "Rotonde Holthuizen" };
        Apply(parade, input);
        db.Parades.Add(parade);
        db.ParadeNumberSequences.Add(new ParadeNumberSequence { ParadeId = parade.Id, LastRegistrationNumber = 0 });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade.created", "Parade", parade.Id.ToString(), null, JsonSerializer.Serialize(input, Json)), cancellationToken);
        return parade;
    }

    /// <summary>
    /// Vaste plekken vooraan (fase 12c). Die krijgen startnummer 1 … n; een groep die al zo'n nummer heeft, blokkeert
    /// het uitbreiden (eerst dat nummer wijzigen).
    /// </summary>
    public async Task SetFixedEntriesAsync(Guid id, IReadOnlyList<ParadeFixedEntry> entries, CancellationToken cancellationToken)
    {
        var parade = await db.Parades.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Optocht niet gevonden.", DomainErrorKind.NotFound);
        if (entries.Count > ParadeFixedEntry.MaxCount)
        {
            throw new DomainException(ErrorCodes.Validation, $"Hooguit {ParadeFixedEntry.MaxCount} vaste plekken.");
        }

        var clean = new List<ParadeFixedEntry>();
        foreach (var e in entries)
        {
            var name = e.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 100 || e.AdultCount is < 0 or > 1000 || e.ChildrenCount is < 0 or > 1000)
            {
                throw new DomainException(ErrorCodes.Validation, "Elke vaste plek heeft een naam (hooguit 100 tekens) en aantallen van 0 tot en met 1000.");
            }

            clean.Add(new ParadeFixedEntry { Name = name, AdultCount = e.AdultCount, ChildrenCount = e.ChildrenCount, HasMusic = e.HasMusic });
        }

        var blocking = await db.ParadeRegistrations.AsNoTracking()
            .Where(r => r.ParadeId == id && r.StartNumber != null && r.StartNumber <= clean.Count)
            .OrderBy(r => r.StartNumber).Select(r => new { r.StartNumber, r.GroupName }).ToListAsync(cancellationToken);
        if (blocking.Count > 0)
        {
            throw new DomainException(ErrorCodes.StartNumberTaken,
                $"Startnummer {string.Join(", ", blocking.Select(b => $"{b.StartNumber} ({b.GroupName})"))} is al aan een groep gegeven. Wijzig dat eerst.",
                DomainErrorKind.Conflict);
        }

        parade.FixedEntries = clean;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade.fixed-entries", "Parade", id.ToString(), null, JsonSerializer.Serialize(clean, Json)), cancellationToken);
    }

    public async Task UpdateAsync(Guid id, ParadeInput input, CancellationToken cancellationToken)
    {
        var parade = await db.Parades.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Optocht niet gevonden.", DomainErrorKind.NotFound);
        await ValidateAsync(input, id, cancellationToken);
        Apply(parade, input);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade.updated", "Parade", id.ToString(), null, JsonSerializer.Serialize(input, Json)), cancellationToken);
    }

    private async Task ValidateAsync(ParadeInput input, Guid? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 100)
        {
            throw new DomainException(ErrorCodes.Validation, "De naam is verplicht (maximaal 100 tekens).");
        }

        if (input.RegistrationClosesAt <= input.RegistrationOpensAt)
        {
            throw new DomainException(ErrorCodes.Validation, "De inschrijving moet sluiten na het openen.");
        }

        if (input.EditDeadlineAt is { } deadline && deadline < input.RegistrationOpensAt)
        {
            throw new DomainException(ErrorCodes.Validation, "De wijzigingsdeadline ligt vóór het openen van de inschrijving.");
        }

        if (input.MaxDocumentsPerRegistration is < 0 or > 20 || input.MaxDocumentSizeMb is < 1 or > 25)
        {
            throw new DomainException(ErrorCodes.Validation, "Documenten: maximaal 20 per inschrijving en 1 tot 25 MB per bestand.");
        }

        if (!await db.CarnivalYears.AnyAsync(y => y.Id == input.CarnivalYearId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.CarnivalYearNotFound, "Carnavalsjaar niet gevonden.");
        }

    }

    private static void Apply(Parade parade, ParadeInput input)
    {
        parade.CarnivalYearId = input.CarnivalYearId;
        parade.Name = input.Name.Trim();
        parade.ParadeDate = input.ParadeDate;
        parade.StartTime = input.StartTime;
        parade.StartLocation = Clean(input.StartLocation);
        parade.RouteDescription = Clean(input.RouteDescription);
        parade.RouteLengthKm = input.RouteLengthKm;
        parade.RegistrationOpensAt = input.RegistrationOpensAt;
        parade.RegistrationClosesAt = input.RegistrationClosesAt;
        parade.EditDeadlineAt = input.EditDeadlineAt;
        parade.SubjectRequired = input.SubjectRequired;
        parade.DefaultSpacingMeters = input.DefaultSpacingMeters;
        parade.MaxDocumentsPerRegistration = input.MaxDocumentsPerRegistration;
        parade.MaxDocumentSizeMb = input.MaxDocumentSizeMb;
        parade.Status = input.Status;
        parade.InfoText = string.IsNullOrWhiteSpace(input.InfoText) ? null : input.InfoText.Trim();
    }

    // ----- Categorieën ------------------------------------------------------------------------------------------------

    /// <summary>Kiesbare categorieën voor een optocht: de globale, waarbij een eigen categorie van de optocht met dezelfde code voorgaat.</summary>
    public async Task<IReadOnlyList<ParadeCategory>> CategoriesForAsync(Guid? paradeId, bool activeOnly, CancellationToken cancellationToken)
    {
        var all = await db.ParadeCategories.AsNoTracking()
            .Where(c => c.ParadeId == null || c.ParadeId == paradeId)
            .ToListAsync(cancellationToken);
        return [.. all.GroupBy(c => c.Code)
            .Select(g => g.OrderByDescending(c => c.ParadeId.HasValue).First())
            .Where(c => !activeOnly || c.Active)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)];
    }

    public async Task<ParadeCategory> SaveCategoryAsync(int? id, CategoryInput input, CancellationToken cancellationToken)
    {
        var code = input.Code.Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z0-9_]{2,40}$") || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100)
        {
            throw new DomainException(ErrorCodes.Validation, "Code (2–40 tekens: hoofdletters, cijfers, _) en naam (maximaal 100 tekens) zijn verplicht.");
        }

        if (input.MinimumParticipants is < 0 || input.MaximumParticipants is < 1
            || (input.MinimumParticipants is { } min && input.MaximumParticipants is { } max && min > max))
        {
            throw new DomainException(ErrorCodes.Validation, "Het minimum mag niet groter zijn dan het maximum.");
        }

        if (await db.ParadeCategories.AnyAsync(c => c.ParadeId == null && c.Code == code && c.Id != id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.CategoryCodeTaken, "Er is al een categorie met deze code.", DomainErrorKind.Conflict);
        }

        ParadeCategory category;
        if (id is { } existing)
        {
            category = await db.ParadeCategories.SingleOrDefaultAsync(c => c.Id == existing, cancellationToken)
                ?? throw new DomainException(ErrorCodes.CategoryNotFound, "Categorie niet gevonden.", DomainErrorKind.NotFound);
        }
        else
        {
            category = new ParadeCategory { Code = code, Name = input.Name.Trim() };
            db.ParadeCategories.Add(category);
        }

        category.Code = code;
        category.Name = input.Name.Trim();
        category.AgeGroup = input.AgeGroup;
        category.Type = input.Type;
        category.MinimumParticipants = input.MinimumParticipants;
        category.MaximumParticipants = input.MaximumParticipants;
        category.ParticipantCountBasis = input.ParticipantCountBasis;
        category.ValidationMode = input.ValidationMode;
        category.HasVehicle = input.HasVehicle;
        category.Active = input.Active;
        category.SortOrder = input.SortOrder;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry(id is null ? "parade-category.created" : "parade-category.updated", "ParadeCategory",
            category.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), null, JsonSerializer.Serialize(input, Json)), cancellationToken);
        return category;
    }

    public DateTime Now => clock.UtcNow.UtcDateTime;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// De huidige optocht (fase 22a): van het actieve carnavalsjaar de eerste die nog niet is afgerond, op datum; zijn ze
/// allemaal afgerond, dan de laatste. Zo wordt een nieuwe optocht in hetzelfde jaar de huidige zodra de vorige klaar is.
/// </summary>
public static class CurrentParade
{
    public static IQueryable<Parade> CurrentParades(this DrammersDbContext db) =>
        db.Parades
            .Where(p => db.CarnivalYears.Any(y => y.Id == p.CarnivalYearId && y.Active))
            .OrderBy(p => p.Status == ParadeStatus.Completed ? 1 : 0)
            .ThenBy(p => p.Status == ParadeStatus.Completed ? (DateOnly?)null : p.ParadeDate)
            .ThenByDescending(p => p.ParadeDate)
            .ThenBy(p => p.CreatedAt);
}
