using Drammers.Infrastructure.Content;
using Drammers.SharedKernel.Errors;

namespace Drammers.Api.Content;

/// <summary>Een carnavalsjaar in het archief, bijvoorbeeld <c>2025-2026</c> (fase 21g).</summary>
public sealed record SeasonResponse(string Slug, DateOnly Start, DateOnly? End);

/// <summary>Het actieve carnavalsjaar en de oudere jaren met inhoud (nieuwste eerst).</summary>
public sealed record SeasonsResponse(SeasonResponse Current, IReadOnlyList<SeasonResponse> Archive);

internal static class SeasonEndpoints
{
    /// <summary>Het gevraagde jaar, of het actieve jaar als er geen is opgegeven; 404 bij een onbekend jaartal.</summary>
    public static async Task<CarnivalSeason> ResolveAsync(CarnivalSeasons seasons, string? slug, CancellationToken cancellationToken)
    {
        var calendar = await seasons.LoadAsync(cancellationToken);
        return slug is null
            ? calendar.Current
            : calendar.Find(slug) ?? throw new DomainException(ErrorCodes.SeasonNotFound, $"Onbekend carnavalsjaar '{slug}'.", DomainErrorKind.NotFound);
    }

    public static SeasonsResponse ToResponse(SeasonCalendar calendar, IReadOnlyList<CarnivalSeason> archive) =>
        new(ToResponse(calendar.Current), [.. archive.Select(ToResponse)]);

    private static SeasonResponse ToResponse(CarnivalSeason s) => new(s.Slug, s.Start, s.End);
}
