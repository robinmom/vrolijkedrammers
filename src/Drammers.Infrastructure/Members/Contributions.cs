using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

public enum ContributionStatus
{
    /// <summary>Betaalt het bedrag.</summary>
    Due,

    /// <summary>Vrijgesteld (bijv. Convent).</summary>
    Exempt,

    /// <summary>Partner in een tweepersoonslidmaatschap: de betaler betaalt.</summary>
    PaidByPartner,

    /// <summary>Soort lidmaatschap onbekend: eerst instellen.</summary>
    Unknown,
}

public sealed record ContributionLine(
    Guid MemberId, string MemberNumber, string FullName, MembershipKind? Kind, bool KindFromEBoekhouden, bool Senior, decimal Amount,
    ContributionStatus Status, string? Note, Guid? PartnerMemberId, string? PartnerName);

public sealed record ContributionTotal(string Label, int Count, decimal Amount);

public sealed record ContributionOverview(
    DateOnly Date, ContributionRate Rate, IReadOnlyList<ContributionLine> Lines, IReadOnlyList<ContributionTotal> Totals, decimal Total);

public sealed record MembershipSettings(MembershipKind? Kind, Guid? PayerMemberId, bool Exempt, string? ExemptReason);

/// <summary>
/// Contributie zonder e-Boekhouden (fase 23a). Per actief lid het soort lidmaatschap; senior (65+) volgt uit de leeftijd op
/// de peildatum (de incassodatum), bij twee personen alleen als beiden 65+ zijn. De partner betaalt niets: dat doet de
/// betaler. Convent en andere vrijstellingen zijn handmatig. Inactieve, geschorste en overleden leden tellen niet mee.
/// </summary>
public sealed class Contributions(DrammersDbContext db, IAuditLogger audit)
{
    public const int SeniorAge = 65;

    /// <summary>Het soort lid uit het gemapte statusveld van e-Boekhouden ("Eénpersoonslid DVD", "Tweepersoonslid DVD", …).</summary>
    public static MembershipKind? KindFromEBoekhouden(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Equals(MembershipWeights.TwoPersons, StringComparison.OrdinalIgnoreCase))
        {
            return MembershipKind.TwoPersons;
        }

        if (value.Contains("dansgarde", StringComparison.OrdinalIgnoreCase))
        {
            return MembershipKind.Dansgarde;
        }

        return value.Contains("persoon", StringComparison.OrdinalIgnoreCase) ? MembershipKind.OnePerson : null;
    }

    public static bool IsSenior(DateOnly? birthDate, DateOnly on) =>
        birthDate is { } b && on.Year - b.Year - (on < b.AddYears(on.Year - b.Year) ? 1 : 0) >= SeniorAge;

    public static decimal Amount(ContributionRate rate, MembershipKind kind, bool senior) => kind switch
    {
        MembershipKind.OnePerson => senior ? rate.OnePersonSenior : rate.OnePerson,
        MembershipKind.TwoPersons => senior ? rate.TwoPersonsSenior : rate.TwoPersons,
        MembershipKind.Dansgarde => rate.Dansgarde,
        _ => 0m,
    };

    public static string KindLabel(MembershipKind? kind, bool senior) => kind switch
    {
        MembershipKind.OnePerson => senior ? "Lid (65+)" : "Lid",
        MembershipKind.TwoPersons => senior ? "Combinatie (65+)" : "Combinatie",
        MembershipKind.Partner => "Combinatie (tweede lid)",
        MembershipKind.Dansgarde => "Dansgarde",
        _ => "Onbekend",
    };

    public async Task<ContributionRate> RateOnAsync(DateOnly date, CancellationToken cancellationToken) =>
        await db.ContributionRates.AsNoTracking().Where(r => r.ValidFrom <= date).OrderByDescending(r => r.ValidFrom).FirstOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.Validation, $"Er is geen tarief dat op {date:dd-MM-yyyy} geldt.");

    public async Task<IReadOnlyList<ContributionRate>> RatesAsync(CancellationToken cancellationToken) =>
        await db.ContributionRates.AsNoTracking().OrderByDescending(r => r.ValidFrom).ToListAsync(cancellationToken);

    /// <summary>Tarief toevoegen of (bij dezelfde ingangsdatum) aanpassen.</summary>
    public async Task SaveRateAsync(ContributionRate input, CancellationToken cancellationToken)
    {
        decimal[] amounts = [input.OnePerson, input.TwoPersons, input.OnePersonSenior, input.TwoPersonsSenior, input.Dansgarde];
        if (amounts.Any(a => a is < 0 or > 10_000 || decimal.Round(a, 2) != a))
        {
            throw new DomainException(ErrorCodes.Validation, "Bedragen liggen tussen € 0 en € 10.000, met hooguit twee decimalen.");
        }

        var rate = await db.ContributionRates.SingleOrDefaultAsync(r => r.ValidFrom == input.ValidFrom, cancellationToken);
        var before = rate is null ? null : JsonSerializer.Serialize(rate, JsonSerializerOptions.Web);
        if (rate is null)
        {
            rate = new ContributionRate { ValidFrom = input.ValidFrom };
            db.ContributionRates.Add(rate);
        }

        (rate.OnePerson, rate.TwoPersons, rate.OnePersonSenior, rate.TwoPersonsSenior, rate.Dansgarde) =
            (input.OnePerson, input.TwoPersons, input.OnePersonSenior, input.TwoPersonsSenior, input.Dansgarde);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("contribution.rate-saved", "ContributionRate", rate.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            before, JsonSerializer.Serialize(rate, JsonSerializerOptions.Web)), cancellationToken);
    }

    public async Task DeleteRateAsync(int id, CancellationToken cancellationToken)
    {
        if (await db.ContributionRates.CountAsync(cancellationToken) <= 1)
        {
            throw new DomainException(ErrorCodes.Validation, "Er moet minstens één tarief blijven.");
        }

        var rate = await db.ContributionRates.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Tarief niet gevonden.", DomainErrorKind.NotFound);
        db.ContributionRates.Remove(rate);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("contribution.rate-deleted", "ContributionRate", id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(rate, JsonSerializerOptions.Web)), cancellationToken);
    }

    /// <summary>Contributie per actief lid op <paramref name="date"/> (de incassodatum), met totalen per soort.</summary>
    public async Task<ContributionOverview> BuildAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var rate = await RateOnAsync(date, cancellationToken);
        var members = await db.Members.AsNoTracking()
            .Where(m => (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active)
            .Select(m => new
            {
                m.Id,
                m.MemberNumber,
                m.FullName,
                m.LastName,
                m.FirstName,
                m.BirthDate,
                m.MembershipKind,
                m.EbStatusRaw,
                m.PayerMemberId,
                m.ContributionExempt,
                m.ContributionExemptReason,
            })
            .ToListAsync(cancellationToken);
        var byId = members.ToDictionary(m => m.Id);
        var partnerOf = members.Where(m => m.PayerMemberId is not null).ToDictionary(m => m.PayerMemberId!.Value);

        var lines = new List<ContributionLine>();
        foreach (var m in members.OrderBy(m => m.LastName ?? m.FullName).ThenBy(m => m.FirstName))
        {
            var fromEb = m.MembershipKind is null;
            var kind = m.MembershipKind ?? KindFromEBoekhouden(m.EbStatusRaw);
            if (kind == MembershipKind.Partner)
            {
                var payer = m.PayerMemberId is { } p && byId.TryGetValue(p, out var x) ? x : null;
                lines.Add(new ContributionLine(m.Id, m.MemberNumber, m.FullName, kind, false, false, 0m, ContributionStatus.PaidByPartner,
                    payer is null ? "Betaler is niet (meer) actief" : $"Betaald door {payer.FullName}", payer?.Id, payer?.FullName));
                continue;
            }

            var partner = partnerOf.GetValueOrDefault(m.Id);
            var senior = kind switch
            {
                MembershipKind.OnePerson => IsSenior(m.BirthDate, date),
                MembershipKind.TwoPersons => partner is not null && IsSenior(m.BirthDate, date) && IsSenior(partner.BirthDate, date),
                _ => false,
            };
            if (kind is null)
            {
                lines.Add(new ContributionLine(m.Id, m.MemberNumber, m.FullName, null, fromEb, false, 0m, ContributionStatus.Unknown,
                    "Soort lidmaatschap onbekend", null, null));
                continue;
            }

            string? note = kind == MembershipKind.TwoPersons && partner is null ? "Tweede persoon nog niet gekoppeld" : null;
            if (m.ContributionExempt)
            {
                lines.Add(new ContributionLine(m.Id, m.MemberNumber, m.FullName, kind, fromEb, senior, 0m, ContributionStatus.Exempt,
                    m.ContributionExemptReason ?? "Vrijgesteld", partner?.Id, partner?.FullName));
                continue;
            }

            if (kind is MembershipKind.OnePerson or MembershipKind.TwoPersons && m.BirthDate is null)
            {
                note = note is null ? "Geboortedatum onbekend: geen seniorentarief" : $"{note}; geboortedatum onbekend";
            }

            lines.Add(new ContributionLine(m.Id, m.MemberNumber, m.FullName, kind, fromEb, senior, Amount(rate, kind.Value, senior), ContributionStatus.Due,
                note, partner?.Id, partner?.FullName));
        }

        var totals = lines.Where(l => l.Status == ContributionStatus.Due)
            .GroupBy(l => KindLabel(l.Kind, l.Senior))
            .Select(g => new ContributionTotal(g.Key, g.Count(), g.Sum(l => l.Amount)))
            .OrderBy(t => t.Label, StringComparer.Ordinal)
            .ToList();
        return new ContributionOverview(date, rate, lines, totals, totals.Sum(t => t.Amount));
    }

    public async Task<MembershipSettings> GetSettingsAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var m = await db.Members.AsNoTracking().SingleOrDefaultAsync(x => x.Id == memberId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
        return new MembershipSettings(m.MembershipKind, m.PayerMemberId, m.ContributionExempt, m.ContributionExemptReason);
    }

    /// <summary>
    /// Soort lidmaatschap, betaler (alleen bij een partner) en vrijstelling instellen. Een betaler heeft hooguit één partner;
    /// wordt een betaler iets anders dan twee personen, dan vervalt de koppeling met zijn partner niet stilzwijgend.
    /// </summary>
    public async Task SetSettingsAsync(Guid memberId, MembershipSettings settings, CancellationToken cancellationToken)
    {
        var member = await db.Members.SingleOrDefaultAsync(x => x.Id == memberId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);

        if (settings.Kind == MembershipKind.Partner)
        {
            if (settings.PayerMemberId is not { } payerId || payerId == memberId)
            {
                throw new DomainException(ErrorCodes.Validation, "Kies het lid dat voor deze partner betaalt.");
            }

            var payer = await db.Members.AsNoTracking().SingleOrDefaultAsync(x => x.Id == payerId, cancellationToken)
                ?? throw new DomainException(ErrorCodes.Validation, "De betaler bestaat niet.");
            if ((payer.MembershipKind ?? KindFromEBoekhouden(payer.EbStatusRaw)) != MembershipKind.TwoPersons)
            {
                throw new DomainException(ErrorCodes.Validation, $"{payer.FullName} heeft geen tweepersoonslidmaatschap.");
            }

            if (await db.Members.AnyAsync(x => x.PayerMemberId == payerId && x.Id != memberId, cancellationToken))
            {
                throw new DomainException(ErrorCodes.Validation, $"Bij {payer.FullName} hoort al een partner.");
            }
        }
        else if (settings.PayerMemberId is not null)
        {
            throw new DomainException(ErrorCodes.Validation, "Alleen een partner heeft een betaler.");
        }

        if (settings.Kind != MembershipKind.TwoPersons
            && await db.Members.AnyAsync(x => x.PayerMemberId == memberId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "Dit lid betaalt voor een partner. Ontkoppel eerst de partner.");
        }

        var before = JsonSerializer.Serialize(new MembershipSettings(member.MembershipKind, member.PayerMemberId, member.ContributionExempt,
            member.ContributionExemptReason), JsonSerializerOptions.Web);
        member.MembershipKind = settings.Kind;
        member.PayerMemberId = settings.Kind == MembershipKind.Partner ? settings.PayerMemberId : null;
        member.ContributionExempt = settings.Exempt;
        member.ContributionExemptReason = settings.Exempt && !string.IsNullOrWhiteSpace(settings.ExemptReason) ? settings.ExemptReason.Trim() : null;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.membership-changed", "Member", memberId.ToString(), before,
            JsonSerializer.Serialize(settings, JsonSerializerOptions.Web)), cancellationToken);
    }
}
