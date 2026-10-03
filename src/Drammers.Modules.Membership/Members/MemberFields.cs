namespace Drammers.Modules.Membership.Members;

/// <summary>Sleutels van de velden uit e-Boekhouden die in het portal bewerkt kunnen worden (<see cref="Member.LocalFields"/>).</summary>
public static class MemberFields
{
    public const string Name = "name";
    public const string Salutation = "salutation";
    public const string Gender = "gender";
    public const string Address = "address";
    public const string PostalCode = "postalCode";
    public const string City = "city";
    public const string Country = "country";
    public const string Email = "email";
    public const string Phone = "phone";
    public const string MobilePhone = "mobilePhone";
    public const string BirthDate = "birthDate";
    public const string JoinYear = "joinYear";
    public const string Category = "category";
    public const string ParadeGroupName = "paradeGroupName";

    /// <summary>IBAN en machtiging (fase 26/23b); via de app gewijzigd = de sync laat ze staan.</summary>
    public const string Iban = "iban";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Name] = "Naam",
        [Salutation] = "Aanhef",
        [Gender] = "Geslacht",
        [Address] = "Adres",
        [PostalCode] = "Postcode",
        [City] = "Plaats",
        [Country] = "Land",
        [Email] = "E-mailadres",
        [Phone] = "Telefoon",
        [MobilePhone] = "Mobiel",
        [BirthDate] = "Geboortedatum",
        [JoinYear] = "Inschrijfjaar",
        [Category] = "Categorie",
        [ParadeGroupName] = "Groep",
        [Iban] = "IBAN en machtiging",
    };
}
