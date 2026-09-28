using Drammers.Modules.Parade.Categories;
using Drammers.Modules.Parade.Registrations;

namespace Drammers.UnitTests.Parade;

/// <summary>Validatie van optochtinschrijvingen (docs/14 §8, besluiten OQ-10/12/13).</summary>
public class RegistrationRulesTests
{
    private static ParadeCategory Category(string code) => ParadeCategory.Seed.Single(c => c.Code == code);

    private static ParadeRegistration Complete(int children = 0, int adults = 12) => new()
    {
        GroupName = "De Knotwilgen",
        ContactName = "Piet Lid",
        ContactPhone = "+31612345678",
        ContactEmail = "piet@example.com",
        CategoryId = 3,
        Subject = "Wilde westen",
        ChildrenCount = children,
        AdultCount = adults,
        BuildAddress = new Address { Street = "Dorpsstraat", HouseNumber = "1", PostalCode = "6999 AA", City = "Loil" },
        EstimatedLengthMeters = 12.5m,
    };

    private static List<ValidationIssue> Blocks(ParadeRegistration r, string code) =>
        [.. RegistrationRules.Validate(r, Category(code), subjectRequired: true, forSubmit: true).Where(i => i.Severity == IssueSeverity.Block)];

    [Fact]
    public void Loopgroep_groot_met_8_deelnemers_is_geblokkeerd()
    {
        var issue = Assert.Single(Blocks(Complete(adults: 8), "ADULT_WALK_L"));
        Assert.Equal(("adultCount", "Deze categorie vereist minimaal 10 volwassenen (nu 8)."), (issue.Field, issue.Message));
    }

    [Fact]
    public void Individueel_of_duo_met_3_deelnemers_is_geblokkeerd()
    {
        var issue = Assert.Single(Blocks(Complete(adults: 3), "ADULT_INDIV"));
        Assert.Equal("Deze categorie staat maximaal 2 volwassenen toe (nu 3).", issue.Message);
    }

    [Fact]
    public void Een_loopgroep_van_10_is_groot_en_niet_klein()
    {
        Assert.Empty(Blocks(Complete(adults: 10), "ADULT_WALK_L"));
        Assert.Single(Blocks(Complete(adults: 10), "ADULT_WALK_S"));
        Assert.Empty(Blocks(Complete(adults: 9), "ADULT_WALK_S"));
    }

    [Fact]
    public void Alleen_de_doelgroep_telt_begeleiders_niet()
    {
        // Jeugdloopgroep klein: 2 kinderen + 6 begeleiders is te weinig kinderen.
        Assert.Equal("Deze categorie vereist minimaal 3 kinderen (nu 2).", Assert.Single(Blocks(Complete(children: 2, adults: 6), "YOUTH_WALK_S")).Message);
        // Duo met één kind erbij: alleen de 2 volwassenen tellen.
        Assert.Empty(Blocks(Complete(children: 1, adults: 2), "ADULT_INDIV"));
    }

    [Fact]
    public void Wagens_geven_een_waarschuwing_en_geen_blokkade()
    {
        var issues = RegistrationRules.Validate(Complete(adults: 0, children: 0), Category("ADULT_TOWED"), true, forSubmit: false);
        Assert.Empty(issues);
        var submit = RegistrationRules.Validate(Complete(adults: 0, children: 0), Category("ADULT_TOWED"), true, forSubmit: true);
        Assert.Equal(IssueSeverity.Block, Assert.Single(submit).Severity);
    }

    [Fact]
    public void Juryadres_alleen_verplicht_als_het_afwijkt_van_het_bouwadres()
    {
        var same = Complete();
        Assert.Empty(Blocks(same, "ADULT_WALK_L"));

        var different = Complete();
        different.JuryInspectionSameAsBuildAddress = false;
        Assert.Equal("juryInspection", Assert.Single(Blocks(different, "ADULT_WALK_L")).Field);

        different.JuryInspectionAddress = new Address { Street = "Schuurweg", HouseNumber = "4", PostalCode = "6999 AB", City = "Loil" };
        Assert.Empty(Blocks(different, "ADULT_WALK_L"));
    }

    [Fact]
    public void Concept_mag_leeg_zijn_maar_indienen_vraagt_alle_verplichte_velden()
    {
        var empty = new ParadeRegistration();
        Assert.Empty(RegistrationRules.Validate(empty, null, true, forSubmit: false));
        var fields = RegistrationRules.Validate(empty, null, true, forSubmit: true).Select(i => i.Field).ToHashSet();
        Assert.Superset(new HashSet<string> { "groupName", "contactName", "contactPhone", "contactEmail", "categoryId", "subject", "buildAddress", "estimatedLengthMeters" }, fields);
    }

    [Fact]
    public void Onderwerp_niet_verplicht_als_de_optocht_dat_zo_instelt()
    {
        var r = Complete();
        r.Subject = null;
        Assert.Empty(RegistrationRules.Validate(r, Category("ADULT_WALK_L"), subjectRequired: false, forSubmit: true));
        Assert.Single(RegistrationRules.Validate(r, Category("ADULT_WALK_L"), subjectRequired: true, forSubmit: true));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(12.345, false)]
    [InlineData(100.01, false)]
    [InlineData(100, true)]
    [InlineData(0.5, true)]
    public void Lengte_groter_dan_0_hoogstens_100_meter_twee_decimalen(double meters, bool valid)
    {
        var r = Complete();
        r.EstimatedLengthMeters = (decimal)meters;
        Assert.Equal(valid, Blocks(r, "ADULT_WALK_L").Count == 0);
    }

    [Theory]
    [InlineData("6999aa", "6999 AA")]
    [InlineData(" 6999 AA ", "6999 AA")]
    [InlineData("0999AA", "0999AA")]
    public void Postcode_wordt_genormaliseerd(string input, string expected) =>
        Assert.Equal(expected, RegistrationRules.NormalizePostalCode(input));

    [Fact]
    public void Standaardbeleid_groep_mag_na_indienen_alleen_beperkt_wijzigen()
    {
        var submitted = DefaultEditPolicy.Seed.Single(p => p.Status == RegistrationStatus.Submitted && p.ActorScope == ActorScope.Owner);
        Assert.Contains("contactPhone", submitted.EditableFields.Split(','));
        Assert.DoesNotContain("groupName", submitted.EditableFields.Split(','));
        Assert.True(submitted.CanWithdraw);
        Assert.Equal("", DefaultEditPolicy.Seed.Single(p => p.Status == RegistrationStatus.Final && p.ActorScope == ActorScope.Owner).EditableFields);
    }
}
