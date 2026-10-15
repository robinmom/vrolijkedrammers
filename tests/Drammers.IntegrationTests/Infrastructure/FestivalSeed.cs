using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Festival;
using Drammers.SharedKernel.Identifiers;

namespace Drammers.IntegrationTests.Infrastructure;

/// <summary>Een actief Dansgardefestival met goedgekeurde dansen, rechtstreeks in de database (programma, jury, uitslag).</summary>
public static class FestivalSeed
{
    public sealed record Dance(string Club, DanceCategory Category, DanceType Type, DanceFormat Format, string Names, ShowStyle? Style = null, string? Title = null);

    public static async Task<(DanceFestival Festival, Dictionary<string, Guid> Entries)> SeedAsync(DrammersDbContext db, params Dance[] dances)
    {
        var festival = new DanceFestival
        {
            Id = IdGenerator.NewId(),
            Name = "Dansgardefestival 2027",
            FestivalDate = new DateOnly(2027, 2, 21),
            Location = "Hart van Loil",
            NotificationEmail = "dansgardefestival@vrolijkedrammers.nl",
            Capacity = 60,
            Active = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.DanceFestivals.Add(festival);
        var entries = new Dictionary<string, Guid>();
        var number = 0;
        foreach (var club in dances.GroupBy(d => d.Club))
        {
            var registration = new DanceFestivalRegistration
            {
                Id = IdGenerator.NewId(),
                FestivalId = festival.Id,
                Number = ++number,
                ClubName = club.Key,
                ContactName = "Contact " + club.Key,
                AddressLine = "Dorpsstraat 1",
                PostalCode = "6999 AA",
                City = "Loil",
                Phone = "0612345678",
                Email = $"{club.Key.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant()}@example.com",
                AccessTokenProtected = "x",
                SubmittedAt = DateTime.UtcNow,
                PrivacyAcceptedAt = DateTime.UtcNow,
                RulesAcceptedAt = DateTime.UtcNow,
            };
            var order = 0;
            foreach (var d in club)
            {
                var entry = new DanceFestivalEntry
                {
                    Id = IdGenerator.NewId(),
                    RegistrationId = registration.Id,
                    SortOrder = ++order,
                    DanceType = d.Type,
                    Format = d.Format,
                    Names = d.Names,
                    AverageAge = d.Category switch { DanceCategory.Minioren => 7, DanceCategory.Junioren => 10, DanceCategory.Jeugd => 14, _ => 18 },
                    DancerCount = d.Format switch { DanceFormat.Solo => 1, DanceFormat.Duo => 2, _ => 8 },
                    LeaderName = "Trainer",
                    Title = d.Title,
                    ShowStyle = d.Style,
                    Category = d.Category,
                    Status = DanceEntryStatus.Approved,
                    HoldsPlace = true,
                };
                registration.Entries.Add(entry);
                entries[d.Names] = entry.Id;
            }

            db.DanceFestivalRegistrations.Add(registration);
        }

        await db.SaveChangesAsync();
        return (festival, entries);
    }
}
