using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Advertisers;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

/// <summary>Gegevens van de vereniging als incassant (niet geheim; de IBAN van de vereniging en het incassant-ID).</summary>
public sealed record SepaCreditor(string? Name, string? Iban, string? CreditorId)
{
    public bool IsComplete => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Iban) && !string.IsNullOrWhiteSpace(CreditorId);
}

public sealed record CollectionPreviewLine(
    Guid MemberId, string MemberNumber, string FullName, string Kind, decimal Amount, string? IbanMasked, string? MandateReference,
    DateOnly? MandateSignedOn, SequenceType SequenceType, string? Warning);

public sealed record CollectionSkip(Guid MemberId, string MemberNumber, string FullName, decimal Amount, string Reason);

public sealed record CollectionPreview(
    DateOnly Date, int Count, decimal Total, IReadOnlyList<CollectionPreviewLine> Lines, IReadOnlyList<CollectionSkip> Skipped,
    IReadOnlyList<string> Warnings, bool CreditorComplete);

public sealed record CollectionRunSummary(
    Guid Id, DateOnly CollectionDate, string Description, string MessageId, int LineCount, decimal Total, DateTime CreatedAt, DateTime? ExportedAt);

/// <summary>
/// SEPA-incasso van de contributie (fase 23c): een voorbeeld op de incassodatum (wie meedoet, wie niet en waarom), een
/// run die bedragen en machtigingen vastlegt, en het bestand pain.008.001.08 voor de bank (Rabo Internetbankieren:
/// Betalen → Incasso's → Bestand aanleveren). De bank controleert het bestand bij het aanleveren; pas na ondertekenen
/// wordt er geïncasseerd.
/// </summary>
public sealed partial class SepaCollections(
    DrammersDbContext db, Contributions contributions, MemberIbanProtector ibans, Advertisers.AdvertiserIbanProtector advertiserIbans, IAuditLogger audit,
    IClock clock, ICurrentActor actor)
{
    /// <summary>Datum voor gemigreerde machtigingen zonder bekende ondertekeningsdatum (gebruikelijk in NL bij de overgang naar SEPA).</summary>
    public static readonly DateOnly MigratedMandateDate = new(2009, 11, 1);

    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.008.001.08";

    public async Task<SepaCreditor> GetCreditorAsync(CancellationToken cancellationToken)
    {
        string[] keys = [AppConfigurationKeys.SepaCreditorName, AppConfigurationKeys.SepaCreditorIban, AppConfigurationKeys.SepaCreditorId];
        var values = await db.AppConfiguration.AsNoTracking().Where(s => keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        string? Get(string key) => values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        return new SepaCreditor(Get(AppConfigurationKeys.SepaCreditorName), Get(AppConfigurationKeys.SepaCreditorIban), Get(AppConfigurationKeys.SepaCreditorId));
    }

    public async Task SetCreditorAsync(SepaCreditor creditor, CancellationToken cancellationToken)
    {
        var name = Sanitize(creditor.Name ?? string.Empty, 70);
        if (name.Length == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul de naam van de vereniging in.");
        }

        var iban = MembershipApplications.NormalizeIban(creditor.Iban);
        var creditorId = (creditor.CreditorId ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (!CreditorIdPattern().IsMatch(creditorId))
        {
            throw new DomainException(ErrorCodes.Validation, "Het incassant-ID is ongeldig (bijv. NL12ZZZ123456780000).");
        }

        var values = new Dictionary<string, string>
        {
            [AppConfigurationKeys.SepaCreditorName] = name,
            [AppConfigurationKeys.SepaCreditorIban] = iban,
            [AppConfigurationKeys.SepaCreditorId] = creditorId,
        };
        var settings = await db.AppConfiguration.Where(s => values.Keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, cancellationToken);
        foreach (var (key, value) in values)
        {
            if (settings.TryGetValue(key, out var setting))
            {
                setting.Value = value;
            }
            else
            {
                db.AppConfiguration.Add(new AppConfigurationSetting { Key = key, Value = value });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("config.sepa-creditor.changed", "AppConfiguration", "sepa", null,
            JsonSerializer.Serialize(values, JsonSerializerOptions.Web)), cancellationToken);
    }

    public async Task<CollectionPreview> PreviewAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var overview = await contributions.BuildAsync(date, cancellationToken);
        var due = overview.Lines.Where(l => l.Status == ContributionStatus.Due && l.Amount > 0).ToList();
        var ids = due.Select(l => l.MemberId).ToList();
        var bank = await db.Members.AsNoTracking().Where(m => ids.Contains(m.Id))
            .Select(m => new { m.Id, m.IbanProtected, m.IbanLast4, m.MandateReference, m.MandateSignedOn })
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        var collected = await CollectedMandatesAsync(cancellationToken);

        var lines = new List<CollectionPreviewLine>();
        var skipped = new List<CollectionSkip>();
        foreach (var l in due)
        {
            var b = bank[l.MemberId];
            if (b.IbanProtected is null || b.MandateReference is null)
            {
                skipped.Add(new CollectionSkip(l.MemberId, l.MemberNumber, l.FullName, l.Amount, "Geen IBAN of machtiging"));
                continue;
            }

            lines.Add(new CollectionPreviewLine(l.MemberId, l.MemberNumber, l.FullName, Contributions.KindLabel(l.Kind, l.Senior), l.Amount,
                MemberIbanProtector.Mask(b.IbanLast4), b.MandateReference, b.MandateSignedOn, Sequence(b.MandateReference, collected),
                b.MandateSignedOn is null ? $"Datum machtiging onbekend: {MigratedMandateDate:dd-MM-yyyy} (gemigreerde machtiging)" : null));
        }

        foreach (var l in overview.Lines.Where(l => l.Status == ContributionStatus.Unknown))
        {
            skipped.Add(new CollectionSkip(l.MemberId, l.MemberNumber, l.FullName, 0, "Soort lidmaatschap onbekend"));
        }

        var warnings = new List<string>();
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        if (date <= today)
        {
            warnings.Add("De incassodatum ligt niet in de toekomst. Kies een werkdag die minstens één werkdag na het aanleveren ligt.");
        }

        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            warnings.Add("De incassodatum valt in het weekend; de bank schuift hem op naar de eerstvolgende werkdag.");
        }

        var creditor = await GetCreditorAsync(cancellationToken);
        if (!creditor.IsComplete)
        {
            warnings.Add("Vul eerst de gegevens van de vereniging in (naam, IBAN en incassant-ID).");
        }

        return new CollectionPreview(date, lines.Count, lines.Sum(l => l.Amount), lines, skipped, warnings, creditor.IsComplete);
    }

    /// <summary>Legt de run vast met bedragen, machtigingen en (versleuteld) de IBAN's van dat moment.</summary>
    public async Task<Guid> CreateAsync(DateOnly date, string? description, CancellationToken cancellationToken)
    {
        if (!(await GetCreditorAsync(cancellationToken)).IsComplete)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul eerst de gegevens van de vereniging in (naam, IBAN en incassant-ID).");
        }

        if (date <= DateOnly.FromDateTime(clock.UtcNow.UtcDateTime))
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een incassodatum in de toekomst.");
        }

        var preview = await PreviewAsync(date, cancellationToken);
        if (preview.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Er is niemand om te incasseren.");
        }

        var text = Sanitize(string.IsNullOrWhiteSpace(description) ? $"Contributie {date.Year} CV De Vrolijke Drammers" : description, 100);
        var runId = IdGenerator.NewId();
        var run = new CollectionRun
        {
            Id = runId,
            CollectionDate = date,
            Description = text,
            MessageId = $"DVD-{date:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}",
            LineCount = preview.Count,
            Total = preview.Total,
            CreatedAt = clock.UtcNow.UtcDateTime,
            CreatedBy = actor.UserId,
        };
        db.CollectionRuns.Add(run);
        var ids = preview.Lines.Select(l => l.MemberId).ToList();
        var protectedIbans = await db.Members.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.IbanProtected!, cancellationToken);
        foreach (var l in preview.Lines)
        {
            db.CollectionRunLines.Add(new CollectionRunLine
            {
                RunId = runId,
                MemberId = l.MemberId,
                MemberNumber = l.MemberNumber,
                DebtorName = Sanitize(l.FullName, 70),
                Amount = l.Amount,
                MandateReference = l.MandateReference!,
                MandateSignedOn = l.MandateSignedOn,
                SequenceType = l.SequenceType,
                IbanLast4 = l.IbanMasked![^4..],
                IbanProtected = protectedIbans[l.MemberId],
                EndToEndId = $"{l.MemberNumber}-{date:yyyyMMdd}"[..Math.Min(35, $"{l.MemberNumber}-{date:yyyyMMdd}".Length)],
                Description = Sanitize($"{text} lidnr {l.MemberNumber}", 140),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("contribution.collection-created", "CollectionRun", runId.ToString(), null,
            JsonSerializer.Serialize(new { date, count = preview.Count, total = preview.Total }, JsonSerializerOptions.Web)), cancellationToken);
        return runId;
    }

    public async Task<IReadOnlyList<CollectionRunSummary>> RunsAsync(CancellationToken cancellationToken, CollectionKind kind = CollectionKind.Contribution) =>
        await db.CollectionRuns.AsNoTracking().Where(r => r.Kind == kind).OrderByDescending(r => r.CreatedAt)
            .Select(r => new CollectionRunSummary(r.Id, r.CollectionDate, r.Description, r.MessageId, r.LineCount, r.Total, r.CreatedAt, r.ExportedAt))
            .ToListAsync(cancellationToken);

    public async Task DeleteAsync(Guid runId, CancellationToken cancellationToken, CollectionKind kind = CollectionKind.Contribution)
    {
        var run = await db.CollectionRuns.SingleOrDefaultAsync(r => r.Id == runId && r.Kind == kind, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Incassorun niet gevonden.", DomainErrorKind.NotFound);
        db.CollectionRuns.Remove(run);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("contribution.collection-deleted", "CollectionRun", runId.ToString(), null, null), cancellationToken);
    }

    /// <summary>Het pain.008.001.08-bestand; per volgordetype (FRST/RCUR) één betaalopdracht.</summary>
    public async Task<(string FileName, byte[] Content)> FileAsync(Guid runId, CancellationToken cancellationToken, CollectionKind kind = CollectionKind.Contribution)
    {
        var run = await db.CollectionRuns.SingleOrDefaultAsync(r => r.Id == runId && r.Kind == kind, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Incassorun niet gevonden.", DomainErrorKind.NotFound);
        var creditor = await GetCreditorAsync(cancellationToken);
        if (!creditor.IsComplete)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul eerst de gegevens van de vereniging in (naam, IBAN en incassant-ID).");
        }

        var lines = await db.CollectionRunLines.AsNoTracking().Where(l => l.RunId == runId).OrderBy(l => l.Id).ToListAsync(cancellationToken);
        static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
        static XElement Agent() => new(Ns + "FinInstnId", new XElement(Ns + "Othr", new XElement(Ns + "Id", "NOTPROVIDED")));

        var groups = lines.GroupBy(l => l.SequenceType).OrderBy(g => g.Key).ToList();
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "Document",
                new XElement(Ns + "CstmrDrctDbtInitn",
                    new XElement(Ns + "GrpHdr",
                        new XElement(Ns + "MsgId", run.MessageId),
                        new XElement(Ns + "CreDtTm", clock.UtcNow.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)),
                        new XElement(Ns + "NbOfTxs", lines.Count),
                        new XElement(Ns + "CtrlSum", Amount(lines.Sum(l => l.Amount))),
                        new XElement(Ns + "InitgPty", new XElement(Ns + "Nm", creditor.Name))),
                    groups.Select((g, i) => new XElement(Ns + "PmtInf",
                        new XElement(Ns + "PmtInfId", $"{run.MessageId}-{i + 1}"),
                        new XElement(Ns + "PmtMtd", "DD"),
                        new XElement(Ns + "BtchBookg", "true"),
                        new XElement(Ns + "NbOfTxs", g.Count()),
                        new XElement(Ns + "CtrlSum", Amount(g.Sum(l => l.Amount))),
                        new XElement(Ns + "PmtTpInf",
                            new XElement(Ns + "SvcLvl", new XElement(Ns + "Cd", "SEPA")),
                            new XElement(Ns + "LclInstrm", new XElement(Ns + "Cd", "CORE")),
                            new XElement(Ns + "SeqTp", g.Key == SequenceType.Frst ? "FRST" : "RCUR")),
                        new XElement(Ns + "ReqdColltnDt", run.CollectionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new XElement(Ns + "Cdtr", new XElement(Ns + "Nm", creditor.Name)),
                        new XElement(Ns + "CdtrAcct", new XElement(Ns + "Id", new XElement(Ns + "IBAN", creditor.Iban))),
                        new XElement(Ns + "CdtrAgt", Agent()),
                        new XElement(Ns + "ChrgBr", "SLEV"),
                        new XElement(Ns + "CdtrSchmeId", new XElement(Ns + "Id", new XElement(Ns + "PrvtId", new XElement(Ns + "Othr",
                            new XElement(Ns + "Id", creditor.CreditorId),
                            new XElement(Ns + "SchmeNm", new XElement(Ns + "Prtry", "SEPA")))))),
                        g.Select(l => new XElement(Ns + "DrctDbtTxInf",
                            new XElement(Ns + "PmtId", new XElement(Ns + "EndToEndId", l.EndToEndId)),
                            new XElement(Ns + "InstdAmt", new XAttribute("Ccy", "EUR"), Amount(l.Amount)),
                            new XElement(Ns + "DrctDbtTx", new XElement(Ns + "MndtRltdInf",
                                new XElement(Ns + "MndtId", l.MandateReference),
                                new XElement(Ns + "DtOfSgntr", (l.MandateSignedOn ?? MigratedMandateDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))),
                            new XElement(Ns + "DbtrAgt", Agent()),
                            new XElement(Ns + "Dbtr", new XElement(Ns + "Nm", l.DebtorName)),
                            new XElement(Ns + "DbtrAcct", new XElement(Ns + "Id", new XElement(Ns + "IBAN", Unprotect(run.Kind, l.IbanProtected)))),
                            new XElement(Ns + "RmtInf", new XElement(Ns + "Ustrd", l.Description)))))))));

        run.ExportedAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("contribution.collection-exported", "CollectionRun", runId.ToString(), null,
            JsonSerializer.Serialize(new { run.LineCount, run.Total }, JsonSerializerOptions.Web)), cancellationToken);

        using var stream = new MemoryStream();
        await using (var writer = System.Xml.XmlWriter.Create(stream, new System.Xml.XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            Async = true,
        }))
        {
            await document.SaveAsync(writer, cancellationToken);
        }

        return ($"incasso-{run.CollectionDate:yyyyMMdd}-{run.MessageId[^8..]}.xml", stream.ToArray());
    }

    // ----- Adverteerders (fase 27c) --------------------------------------------------------------------------------

    /// <summary>
    /// Wie van de adverteerders in de incasso komt: in het campagnejaar opgehaald, betaling per machtiging, met een
    /// bedrag. Wie al in een incasso van dat jaar zat, wordt overgeslagen (niet dubbel incasseren).
    /// </summary>
    public async Task<CollectionPreview> PreviewAdvertisersAsync(DateOnly date, int year, CancellationToken cancellationToken)
    {
        var rows = await db.AdvertiserYears.AsNoTracking()
            .Where(y => y.Year == year && y.Status == AdvertiserYearStatus.Collected && !y.IsFree && y.Amount > 0)
            .Join(db.Advertisers.Where(a => a.Active && a.Payment == AdvertiserPayment.Mandate), y => y.AdvertiserId, a => a.Id, (y, a) => new { a, y.Amount })
            .OrderBy(x => x.a.Number).ToListAsync(cancellationToken);
        var done = (await db.CollectionRunLines.AsNoTracking()
            .Join(db.CollectionRuns.Where(r => r.Kind == CollectionKind.Advertisers && r.CampaignYear == year), l => l.RunId, r => r.Id, (l, _) => l.AdvertiserId)
            .ToListAsync(cancellationToken)).OfType<Guid>().ToHashSet();
        var collected = await CollectedMandatesAsync(cancellationToken);

        var lines = new List<CollectionPreviewLine>();
        var skipped = new List<CollectionSkip>();
        foreach (var (a, amount) in rows.Select(r => (r.a, r.Amount!.Value)))
        {
            var number = a.Number.ToString(CultureInfo.InvariantCulture);
            if (done.Contains(a.Id))
            {
                skipped.Add(new CollectionSkip(a.Id, number, a.CompanyName, amount, $"Al in een incasso van {year}"));
            }
            else if (a.IbanProtected is null || a.MandateReference is null)
            {
                skipped.Add(new CollectionSkip(a.Id, number, a.CompanyName, amount, a.IbanProtected is null ? "Geen IBAN" : "Geen machtigingsnummer"));
            }
            else
            {
                var signed = AdvertiserMandateDate(a.MandateReference);
                lines.Add(new CollectionPreviewLine(a.Id, number, a.CompanyName, AdvertiserKindLabel(a.Kind), amount, $"**** {a.IbanLast4}",
                    a.MandateReference, signed, Sequence(a.MandateReference, collected),
                    signed is null ? $"Datum machtiging onbekend: {MigratedMandateDate:dd-MM-yyyy} (gemigreerde machtiging)" : null));
            }
        }

        var warnings = new List<string>();
        if (date <= DateOnly.FromDateTime(clock.UtcNow.UtcDateTime))
        {
            warnings.Add("De incassodatum ligt niet in de toekomst. Kies een werkdag die minstens één werkdag na het aanleveren ligt.");
        }

        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            warnings.Add("De incassodatum valt in het weekend; de bank schuift hem op naar de eerstvolgende werkdag.");
        }

        var creditor = await GetCreditorAsync(cancellationToken);
        if (!creditor.IsComplete)
        {
            warnings.Add("Vul eerst de gegevens van de vereniging in (naam, IBAN en incassant-ID) onder Leden → Incasso.");
        }

        return new CollectionPreview(date, lines.Count, lines.Sum(l => l.Amount), lines, skipped, warnings, creditor.IsComplete);
    }

    /// <summary>Legt de incasso van de adverteerders vast (zoals bij de contributie) en geeft de run terug.</summary>
    public async Task<Guid> CreateAdvertisersAsync(DateOnly date, int year, string? description, CancellationToken cancellationToken)
    {
        if (!(await GetCreditorAsync(cancellationToken)).IsComplete)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul eerst de gegevens van de vereniging in (naam, IBAN en incassant-ID) onder Leden → Incasso.");
        }

        if (date <= DateOnly.FromDateTime(clock.UtcNow.UtcDateTime))
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een incassodatum in de toekomst.");
        }

        var preview = await PreviewAdvertisersAsync(date, year, cancellationToken);
        if (preview.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Er is geen adverteerder om te incasseren.");
        }

        var text = Sanitize(string.IsNullOrWhiteSpace(description) ? $"Drammerskrant {year} CV De Vrolijke Drammers" : description, 100);
        var runId = IdGenerator.NewId();
        db.CollectionRuns.Add(new CollectionRun
        {
            Id = runId,
            Kind = CollectionKind.Advertisers,
            CampaignYear = year,
            CollectionDate = date,
            Description = text,
            MessageId = $"DVDADV-{date:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}",
            LineCount = preview.Count,
            Total = preview.Total,
            CreatedAt = clock.UtcNow.UtcDateTime,
            CreatedBy = actor.UserId,
        });
        var ids = preview.Lines.Select(l => l.MemberId).ToList();
        var protectedIbans = await db.Advertisers.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.IbanProtected!, cancellationToken);
        foreach (var l in preview.Lines)
        {
            var endToEnd = $"ADV{l.MemberNumber}-{date:yyyyMMdd}";
            db.CollectionRunLines.Add(new CollectionRunLine
            {
                RunId = runId,
                AdvertiserId = l.MemberId,
                MemberNumber = $"ADV{l.MemberNumber}"[..Math.Min(15, $"ADV{l.MemberNumber}".Length)],
                DebtorName = Sanitize(l.FullName, 70),
                Amount = l.Amount,
                MandateReference = l.MandateReference!,
                MandateSignedOn = l.MandateSignedOn,
                SequenceType = l.SequenceType,
                IbanLast4 = l.IbanMasked![^4..],
                IbanProtected = protectedIbans[l.MemberId],
                EndToEndId = endToEnd[..Math.Min(35, endToEnd.Length)],
                Description = Sanitize($"{l.Kind} {text} nr {l.MemberNumber}", 140),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.collection-created", "CollectionRun", runId.ToString(), null,
            JsonSerializer.Serialize(new { date, year, count = preview.Count, total = preview.Total }, JsonSerializerOptions.Web)), cancellationToken);
        return runId;
    }

    private string Unprotect(CollectionKind kind, string value) =>
        kind == CollectionKind.Advertisers ? advertiserIbans.Unprotect(value) : ibans.Unprotect(value);

    private static string AdvertiserKindLabel(AdvertiserKind kind) => kind switch
    {
        AdvertiserKind.Advertisement => "Advertentie",
        AdvertiserKind.FreeGift => "Vrije gift",
        _ => "Gift",
    };

    /// <summary>Datum uit een machtiging die via de app is gegeven (DVD-ADV-nummer-jjjjmmdd); anders onbekend.</summary>
    private static DateOnly? AdvertiserMandateDate(string mandate) =>
        AppMandatePattern().Match(mandate) is { Success: true } m && DateOnly.TryParseExact(m.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    /// <summary>
    /// FRST voor een machtiging die via de app is gegeven (kenmerk DVD-lidnummer-datum) en nog nooit in een run zat; anders
    /// RCUR. Machtigingen uit e-Boekhouden zijn al eerder geïncasseerd.
    /// </summary>
    private static SequenceType Sequence(string mandate, HashSet<string> collected) =>
        AppMandatePattern().IsMatch(mandate) && !collected.Contains(mandate) ? SequenceType.Frst : SequenceType.Rcur;

    private async Task<HashSet<string>> CollectedMandatesAsync(CancellationToken cancellationToken) =>
        [.. await db.CollectionRunLines.AsNoTracking().Join(db.CollectionRuns.Where(r => r.ExportedAt != null), l => l.RunId, r => r.Id, (l, _) => l.MandateReference)
            .Distinct().ToListAsync(cancellationToken)];

    /// <summary>De SEPA-tekenset (Latin): accenten weg, andere tekens worden een spatie.</summary>
    public static string Sanitize(string value, int max)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsAsciiLetterOrDigit(c) || "/-?:().,'+ ".Contains(c, StringComparison.Ordinal) ? c : ' ');
        }

        var clean = MultipleSpaces().Replace(builder.ToString(), " ").Trim();
        return clean.Length > max ? clean[..max] : clean;
    }

    [GeneratedRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]{3}[A-Z0-9]{1,28}$")]
    private static partial Regex CreditorIdPattern();

    // Machtigingen via de app: leden DVD-lidnummer-datum, adverteerders DVD-ADV-nummer-datum (fase 27c).
    [GeneratedRegex(@"^DVD-(?:ADV-)?\d+-(\d{8})$")]
    private static partial Regex AppMandatePattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultipleSpaces();
}
