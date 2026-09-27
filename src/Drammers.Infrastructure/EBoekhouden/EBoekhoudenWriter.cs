using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.EBoekhouden;

/// <summary>Resultaat van het aanmaken: lidnummer en (bij echt aanmaken) het e-Boekhouden-ID.</summary>
public sealed record EbCreatedMember(int? EbMemberId, string MemberNumber, bool Simulated);

/// <summary>Maakt een nieuw lid aan in e-Boekhouden (fase 9b, ADR-014), of vindt het al aangemaakte lid terug.</summary>
public interface IEBoekhoudenWriter
{
    Task<EbCreatedMember> CreateOrFindMemberAsync(EbNewMember member, CancellationToken cancellationToken);
}

/// <summary>
/// Echt aanmaken via <c>POST /v1/member</c>. Idempotent: eerst zoeken op e-mailadres én naam (broertjes en zusjes delen
/// vaak het e-mailadres van de ouder), zodat een herhaalde saga-stap geen tweede lid maakt.
/// </summary>
internal sealed class EBoekhoudenWriter(IEBoekhoudenClient client) : IEBoekhoudenWriter
{
    public async Task<EbCreatedMember> CreateOrFindMemberAsync(EbNewMember member, CancellationToken cancellationToken)
    {
        await using var session = await client.OpenSessionAsync(cancellationToken);
        foreach (var candidate in await session.FindMembersByEmailAsync(member.EmailAddress, cancellationToken))
        {
            var existing = await session.GetMemberAsync(candidate.Id, cancellationToken);
            if (string.Equals(existing.Name?.Trim(), member.Name.Trim(), StringComparison.OrdinalIgnoreCase) && existing.MemberNumber is { } number)
            {
                return new EbCreatedMember(existing.Id, number, Simulated: false);
            }
        }

        var created = await session.CreateMemberAsync(member, cancellationToken);
        return new EbCreatedMember(created.Id, created.MemberNumber
            ?? throw new EBoekhoudenException("e-Boekhouden gaf geen lidnummer terug."), Simulated: false);
    }
}

/// <summary>
/// Dev (OQ-04): de echte administratie wordt niet beschreven. Geeft een herkenbaar tijdelijk lidnummer (<c>SIM…</c>); een
/// volgende ledensync markeert zo'n lid als "ontbreekt in e-Boekhouden".
/// </summary>
internal sealed class SimulatedEBoekhoudenWriter : IEBoekhoudenWriter
{
    public const string Prefix = "SIM";

    public Task<EbCreatedMember> CreateOrFindMemberAsync(EbNewMember member, CancellationToken cancellationToken) =>
        Task.FromResult(new EbCreatedMember(null, Prefix + RandomNumberGenerator.GetInt32(100_000, 1_000_000), Simulated: true));
}

internal static class EBoekhoudenWriterSelector
{
    public static IEBoekhoudenWriter Select(IOptions<EBoekhoudenOptions> options, IEBoekhoudenClient client) =>
        options.Value.WriteEnabled ? new EBoekhoudenWriter(client) : new SimulatedEBoekhoudenWriter();
}
