using System.Globalization;
using System.Text.RegularExpressions;
using Drammers.Modules.Parade.Categories;

namespace Drammers.Modules.Parade.Registrations;

public enum IssueSeverity
{
    Block,
    Warn,
}

/// <summary>Een validatiemelding bij een veld (camelCase, zoals de app het veld noemt).</summary>
public sealed record ValidationIssue(string Field, string Message, IssueSeverity Severity);

/// <summary>Veldnamen voor validatie, historie en het statusbeleid.</summary>
public static class RegistrationFields
{
    public const string GroupName = "groupName";
    public const string ContactName = "contactName";
    public const string ContactPhone = "contactPhone";
    public const string ContactEmail = "contactEmail";
    public const string Category = "categoryId";
    public const string Subject = "subject";
    public const string SubjectDescription = "subjectDescription";
    public const string ChildrenCount = "childrenCount";
    public const string AdultCount = "adultCount";
    public const string BuildAddress = "buildAddress";
    public const string JuryInspection = "juryInspection";
    public const string EstimatedLength = "estimatedLengthMeters";
    public const string AdditionalInformation = "additionalInformation";
    public const string Documents = "documents";

    public static readonly string[] All =
    [
        GroupName, ContactName, ContactPhone, ContactEmail, Category, Subject, SubjectDescription, ChildrenCount, AdultCount,
        BuildAddress, JuryInspection, EstimatedLength, AdditionalInformation, Documents,
    ];

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [GroupName] = "Groepsnaam",
        [ContactName] = "Contactpersoon",
        [ContactPhone] = "Telefoon",
        [ContactEmail] = "E-mailadres",
        [Category] = "Categorie",
        [Subject] = "Onderwerp",
        [SubjectDescription] = "Toelichting onderwerp",
        [ChildrenCount] = "Aantal kinderen",
        [AdultCount] = "Aantal volwassenen",
        [BuildAddress] = "Bouwadres",
        [JuryInspection] = "Stalling jury",
        [EstimatedLength] = "Geschatte lengte",
        [AdditionalInformation] = "Extra informatie",
        [Documents] = "Documenten",
    };
}

/// <summary>
/// Validatieregels van een inschrijving (docs/13 §5, docs/14 §8). Puur: zonder database, zodat app, website en server
/// dezelfde uitkomst geven. Het telefoonnummer is vooraf genormaliseerd (libphonenumber, in de infrastructuur).
/// </summary>
public static partial class RegistrationRules
{
    public const int MaxLengthMeters = 100;

    /// <param name="r">De inschrijving.</param>
    /// <param name="category">De gekozen categorie, of <c>null</c>.</param>
    /// <param name="subjectRequired">Onderwerp verplicht (instelling van de optocht, OQ-13).</param>
    /// <param name="forSubmit">Bij indienen zijn alle verplichte velden nodig; een concept mag leeg zijn.</param>
    public static List<ValidationIssue> Validate(ParadeRegistration r, ParadeCategory? category, bool subjectRequired, bool forSubmit)
    {
        var issues = new List<ValidationIssue>();
        void Block(string field, string message) => issues.Add(new ValidationIssue(field, message, IssueSeverity.Block));

        Text(r.GroupName, RegistrationFields.GroupName, "de groepsnaam", 2, 100, forSubmit, issues);
        Text(r.ContactName, RegistrationFields.ContactName, "de naam van de contactpersoon", 2, 100, forSubmit, issues);
        if (string.IsNullOrWhiteSpace(r.ContactPhone))
        {
            if (forSubmit)
            {
                Block(RegistrationFields.ContactPhone, "Vul een telefoonnummer in.");
            }
        }

        if (string.IsNullOrWhiteSpace(r.ContactEmail))
        {
            if (forSubmit)
            {
                Block(RegistrationFields.ContactEmail, "Vul een e-mailadres in.");
            }
        }
        else if (r.ContactEmail.Length > 254 || !EmailPattern().IsMatch(r.ContactEmail))
        {
            Block(RegistrationFields.ContactEmail, "Vul een geldig e-mailadres in.");
        }

        if (r.ChildrenCount < 0 || r.AdultCount < 0)
        {
            Block(RegistrationFields.AdultCount, "Aantallen kunnen niet negatief zijn.");
        }

        if (category is null)
        {
            if (forSubmit)
            {
                Block(RegistrationFields.Category, "Kies een categorie.");
            }
        }
        else
        {
            Participants(r, category, forSubmit, issues);
        }

        if (subjectRequired || !string.IsNullOrWhiteSpace(r.Subject))
        {
            Text(r.Subject, RegistrationFields.Subject, "het onderwerp", 2, 150, forSubmit && subjectRequired, issues);
        }

        if (r.SubjectDescription?.Length > 2000)
        {
            Block(RegistrationFields.SubjectDescription, "De toelichting is maximaal 2000 tekens.");
        }

        AddressIssues(r.BuildAddress, RegistrationFields.BuildAddress, "bouwadres", forSubmit, issues);
        if (!r.JuryInspectionSameAsBuildAddress)
        {
            AddressIssues(r.JuryInspectionAddress, RegistrationFields.JuryInspection, "adres van de stalling voor de jury", forSubmit, issues);
        }

        if (r.EstimatedLengthMeters is { } length)
        {
            if (length <= 0 || length > MaxLengthMeters || decimal.Round(length, 1) != length)
            {
                Block(RegistrationFields.EstimatedLength, $"De lengte is groter dan 0 en hoogstens {MaxLengthMeters} meter, met maximaal 1 decimaal.");
            }
        }
        else if (forSubmit)
        {
            Block(RegistrationFields.EstimatedLength, "Vul de geschatte lengte in (inclusief trekkend voertuig).");
        }

        if (r.AdditionalInformation?.Length > 4000)
        {
            Block(RegistrationFields.AdditionalInformation, "De extra informatie is maximaal 4000 tekens.");
        }

        return issues;
    }

    /// <summary>
    /// Aantal in de doelgroep binnen de grenzen van de categorie (OQ-10: alleen de doelgroep telt). Minimaal één deelnemer
    /// in de doelgroep is altijd verplicht; de ernst van min/max volgt uit de categorie.
    /// </summary>
    private static void Participants(ParadeRegistration r, ParadeCategory category, bool forSubmit, List<ValidationIssue> issues)
    {
        var count = category.CountOf(r.ChildrenCount, r.AdultCount);
        var who = category.ParticipantCountBasis switch
        {
            ParticipantCountBasis.ChildrenOnly => "kinderen",
            ParticipantCountBasis.AdultsOnly => "volwassenen",
            _ => "deelnemers",
        };
        var field = category.ParticipantCountBasis == ParticipantCountBasis.ChildrenOnly ? RegistrationFields.ChildrenCount : RegistrationFields.AdultCount;
        if (count < 1)
        {
            if (forSubmit)
            {
                issues.Add(new ValidationIssue(field, $"Deze categorie vraagt minimaal 1 deelnemer ({who}).", IssueSeverity.Block));
            }

            return;
        }

        if (category.ValidationMode == ValidationMode.None)
        {
            return;
        }

        var severity = category.ValidationMode == ValidationMode.Block ? IssueSeverity.Block : IssueSeverity.Warn;
        if (category.MinimumParticipants is { } min && count < min)
        {
            issues.Add(new ValidationIssue(field, $"Deze categorie vereist minimaal {min} {who} (nu {count}).", severity));
        }

        if (category.MaximumParticipants is { } max && count > max)
        {
            issues.Add(new ValidationIssue(field, $"Deze categorie staat maximaal {max} {who} toe (nu {count}).", severity));
        }
    }

    private static void Text(string? value, string field, string what, int min, int max, bool required, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                issues.Add(new ValidationIssue(field, $"Vul {what} in.", IssueSeverity.Block));
            }

            return;
        }

        var length = value.Trim().Length;
        if (length < min || length > max)
        {
            issues.Add(new ValidationIssue(field, $"{char.ToUpper(what[0], CultureInfo.InvariantCulture)}{what[1..]} is {min} tot {max} tekens.", IssueSeverity.Block));
        }
        else if (value.Contains('<') || value.Contains('>'))
        {
            issues.Add(new ValidationIssue(field, "Gebruik geen < of > in de tekst.", IssueSeverity.Block));
        }
    }

    private static void AddressIssues(Address address, string field, string what, bool forSubmit, List<ValidationIssue> issues)
    {
        if (address.IsEmpty)
        {
            if (forSubmit)
            {
                issues.Add(new ValidationIssue(field, $"Vul het {what} in.", IssueSeverity.Block));
            }

            return;
        }

        if (forSubmit && (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.HouseNumber)
            || string.IsNullOrWhiteSpace(address.PostalCode) || string.IsNullOrWhiteSpace(address.City)))
        {
            issues.Add(new ValidationIssue(field, $"Vul straat, huisnummer, postcode en plaats van het {what} in.", IssueSeverity.Block));
        }

        if (!string.IsNullOrWhiteSpace(address.PostalCode) && address.Country == "NL" && !PostalCodePattern().IsMatch(address.PostalCode.Trim()))
        {
            issues.Add(new ValidationIssue(field, "Vul een geldige Nederlandse postcode in, bijvoorbeeld 6999 AA.", IssueSeverity.Block));
        }

        if (!string.IsNullOrWhiteSpace(address.HouseNumber) && !HouseNumberPattern().IsMatch(address.HouseNumber.Trim()))
        {
            issues.Add(new ValidationIssue(field, "Het huisnummer is een getal van 1 tot 99999.", IssueSeverity.Block));
        }

        if (address.Addition?.Length > 10)
        {
            issues.Add(new ValidationIssue(field, "De toevoeging is maximaal 10 tekens.", IssueSeverity.Block));
        }
    }

    /// <summary>Postcode als "1234 AB".</summary>
    public static string? NormalizePostalCode(string? value)
    {
        var trimmed = value?.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);
        return trimmed is { Length: 6 } && PostalCodePattern().IsMatch(trimmed) ? $"{trimmed[..4]} {trimmed[4..]}" : value?.Trim();
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex("^[1-9][0-9]{3} ?[A-Za-z]{2}$")]
    private static partial Regex PostalCodePattern();

    [GeneratedRegex("^[1-9][0-9]{0,4}$")]
    private static partial Regex HouseNumberPattern();
}

/// <summary>Standaard statusbeleid (docs/14 §9).</summary>
public static class DefaultEditPolicy
{
    private const string All = "*";

    private static readonly string Contact = string.Join(',', RegistrationFields.ContactName, RegistrationFields.ContactPhone, RegistrationFields.ContactEmail);

    private static readonly string SubmittedOwner = string.Join(',', Contact, RegistrationFields.ChildrenCount, RegistrationFields.AdultCount,
        RegistrationFields.AdditionalInformation, RegistrationFields.Documents, RegistrationFields.EstimatedLength);

    public static IReadOnlyList<ParadeStatusEditPolicy> Seed
    {
        get
        {
            var id = 0;
            var rows = new List<ParadeStatusEditPolicy>();
            void Add(RegistrationStatus status, ActorScope scope, string fields, bool withdraw) =>
                rows.Add(new ParadeStatusEditPolicy { Id = ++id, Status = status, ActorScope = scope, EditableFields = fields, CanWithdraw = withdraw });

            Add(RegistrationStatus.Draft, ActorScope.Owner, All, false);
            Add(RegistrationStatus.Submitted, ActorScope.Owner, SubmittedOwner, true);
            Add(RegistrationStatus.UnderReview, ActorScope.Owner, string.Join(',', Contact, RegistrationFields.AdditionalInformation, RegistrationFields.Documents), true);
            // De commissie vraagt om een aanvulling: de groep mag dan alles aanpassen en dient de aanvulling opnieuw in.
            Add(RegistrationStatus.AdditionalInformationRequired, ActorScope.Owner, All, true);
            Add(RegistrationStatus.Approved, ActorScope.Owner, string.Join(',', Contact, RegistrationFields.Documents), true);
            Add(RegistrationStatus.StartNumberAssigned, ActorScope.Owner, Contact, true);
            Add(RegistrationStatus.Final, ActorScope.Owner, "", false);
            Add(RegistrationStatus.Rejected, ActorScope.Owner, "", false);
            Add(RegistrationStatus.Withdrawn, ActorScope.Owner, "", false);
            foreach (var status in Enum.GetValues<RegistrationStatus>())
            {
                Add(status, ActorScope.Committee, status == RegistrationStatus.Final ? "" : All, status != RegistrationStatus.Final);
                Add(status, ActorScope.SpecialAdmin, All, true);
            }

            return rows;
        }
    }
}
