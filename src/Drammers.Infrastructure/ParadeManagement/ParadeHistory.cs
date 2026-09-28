using System.Globalization;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Drammers.Infrastructure.ParadeManagement;

/// <summary>Bron van een wijziging (app, website, portal) voor de veldhistorie; per request of worker-scope gezet.</summary>
public sealed class ParadeChangeContext
{
    public RegistrationSource Source { get; set; } = RegistrationSource.Portal;
}

/// <summary>
/// Legt elke wijziging van een optochtinschrijving per veld vast in <c>ParadeRegistrationHistory</c> (docs/14 §6), ook
/// als die later uit het portal of een import komt. Eén opslagactie krijgt één correlatie-id.
/// </summary>
internal sealed class ParadeHistoryInterceptor(IClock clock, ICurrentActor actor, ParadeChangeContext context) : SaveChangesInterceptor
{
    private static readonly HashSet<string> Ignored =
        [nameof(ParadeRegistration.RowVersion), nameof(ParadeRegistration.UpdatedAt), nameof(ParadeRegistration.UpdatedBy),
         nameof(ParadeRegistration.ValidationWarnings), nameof(ParadeRegistration.DeadlineReminderSentAt)];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Record(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Record(DbContext? db)
    {
        if (db is not DrammersDbContext drammers)
        {
            return;
        }

        var rows = new List<ParadeRegistrationHistory>();
        var correlation = Guid.NewGuid();
        var now = clock.UtcNow.UtcDateTime;
        foreach (var entry in drammers.ChangeTracker.Entries<ParadeRegistration>().Where(e => e.State == EntityState.Modified))
        {
            foreach (var property in entry.Properties.Where(p => p.IsModified && !Ignored.Contains(p.Metadata.Name)))
            {
                Add(rows, entry.Entity.Id, property.Metadata.Name, property.OriginalValue, property.CurrentValue, now, correlation);
            }

            // Adressen zijn owned types: hun wijzigingen staan op de owned entry.
            foreach (var reference in entry.References.Where(r => r.TargetEntry is not null))
            {
                foreach (var property in reference.TargetEntry!.Properties.Where(p => p.IsModified))
                {
                    Add(rows, entry.Entity.Id, $"{reference.Metadata.Name}.{property.Metadata.Name}", property.OriginalValue, property.CurrentValue, now, correlation);
                }
            }
        }

        drammers.ParadeRegistrationHistory.AddRange(rows);
    }

    private void Add(List<ParadeRegistrationHistory> rows, Guid registrationId, string field, object? oldValue, object? newValue, DateTime now, Guid correlation)
    {
        var (before, after) = (Format(oldValue), Format(newValue));
        if (before == after)
        {
            return;
        }

        rows.Add(new ParadeRegistrationHistory
        {
            RegistrationId = registrationId,
            FieldName = field,
            FieldLabel = Label(field),
            OldValue = before,
            NewValue = after,
            ChangedByUserId = actor.UserId,
            ChangedAt = now,
            ChangeSource = context.Source,
            CorrelationId = correlation,
        });
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static string Label(string field) => field switch
    {
        nameof(ParadeRegistration.GroupName) => "Groepsnaam",
        nameof(ParadeRegistration.ContactName) => "Contactpersoon",
        nameof(ParadeRegistration.ContactPhone) => "Telefoon",
        nameof(ParadeRegistration.ContactEmail) => "E-mailadres",
        nameof(ParadeRegistration.CategoryId) => "Categorie",
        nameof(ParadeRegistration.Subject) => "Onderwerp",
        nameof(ParadeRegistration.SubjectDescription) => "Toelichting onderwerp",
        nameof(ParadeRegistration.ChildrenCount) => "Aantal kinderen",
        nameof(ParadeRegistration.AdultCount) => "Aantal volwassenen",
        nameof(ParadeRegistration.JuryInspectionSameAsBuildAddress) => "Stalling jury gelijk aan bouwadres",
        nameof(ParadeRegistration.EstimatedLengthMeters) => "Geschatte lengte",
        nameof(ParadeRegistration.MeasuredLengthMeters) => "Gemeten lengte",
        nameof(ParadeRegistration.AdditionalInformation) => "Extra informatie",
        nameof(ParadeRegistration.Status) => "Status",
        nameof(ParadeRegistration.RegistrationNumber) => "Opgavenummer",
        nameof(ParadeRegistration.StartNumber) => "Startnummer",
        nameof(ParadeRegistration.ParadeOrder) => "Volgorde",
        nameof(ParadeRegistration.SubmittedAt) => "Ingediend op",
        nameof(ParadeRegistration.WithdrawnAt) => "Ingetrokken op",
        _ when field.StartsWith("BuildAddress.", StringComparison.Ordinal) => $"Bouwadres ({field[13..]})",
        _ when field.StartsWith("JuryInspectionAddress.", StringComparison.Ordinal) => $"Adres stalling jury ({field[22..]})",
        _ => field,
    };
}

/// <summary>Telefoonnummers (libphonenumber): Nederlandse nummers zonder landcode, en nummers uit België en Duitsland.</summary>
public static class PhoneNormalizer
{
    private static readonly PhoneNumbers.PhoneNumberUtil Util = PhoneNumbers.PhoneNumberUtil.GetInstance();
    private static readonly HashSet<string> Regions = ["NL", "BE", "DE"];

    /// <returns>E.164 (<c>+31612345678</c>) of <c>null</c> als het geen geldig nummer is.</returns>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        try
        {
            var number = Util.Parse(input, "NL");
            return Util.IsValidNumber(number) && Regions.Contains(Util.GetRegionCodeForNumber(number) ?? "")
                ? Util.Format(number, PhoneNumbers.PhoneNumberFormat.E164)
                : null;
        }
        catch (PhoneNumbers.NumberParseException)
        {
            return null;
        }
    }

    /// <summary>Leesbaar voor weergave: nationaal voor NL (<c>06 12345678</c>), anders internationaal.</summary>
    public static string Display(string e164)
    {
        try
        {
            var number = Util.Parse(e164, "NL");
            return Util.Format(number, Util.GetRegionCodeForNumber(number) == "NL" ? PhoneNumbers.PhoneNumberFormat.NATIONAL : PhoneNumbers.PhoneNumberFormat.INTERNATIONAL);
        }
        catch (PhoneNumbers.NumberParseException)
        {
            return e164;
        }
    }
}
