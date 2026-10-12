using Drammers.Infrastructure.Members;

namespace Drammers.UnitTests.Members;

public class JoinYearTests
{
    [Theory]
    [InlineData(2026, 2026, 2, 17)]
    [InlineData(2027, 2027, 2, 9)]
    [InlineData(2028, 2028, 2, 29)]
    [InlineData(2038, 2038, 3, 9)]
    public void Carnavalsdinsdag_is_Pasen_min_47_dagen(int year, int expectedYear, int month, int day) =>
        Assert.Equal(new DateOnly(expectedYear, month, day), JoinYears.CarnivalTuesday(year));

    [Theory]
    [InlineData("2026-01-10", 2026)]
    [InlineData("2026-02-17", 2026)] // laatste carnavalsdag telt nog mee
    [InlineData("2026-02-18", 2027)]
    [InlineData("2026-10-12", 2027)]
    [InlineData("2027-02-09", 2027)]
    [InlineData("2027-02-10", 2028)]
    [InlineData("2027-12-31", 2028)]
    public void Inschrijfjaar_is_het_eerste_carnaval_dat_het_lid_meeviert(string date, short expected) =>
        Assert.Equal(expected, JoinYears.ForDate(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)));
}
