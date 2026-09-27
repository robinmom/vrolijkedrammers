using Drammers.Worker.Scheduling;

namespace Drammers.UnitTests.Worker;

public class JobScheduleTests
{
    private static readonly TimeZoneInfo Loil = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    [Theory]
    // Zomertijd (UTC+2): 02:30 lokaal → vandaag 03:00 lokaal = 01:00 UTC.
    [InlineData("2026-07-01T00:30:00Z", "2026-07-01T01:00:00Z")]
    // Na 03:00 lokaal → morgen.
    [InlineData("2026-07-01T01:00:00Z", "2026-07-02T01:00:00Z")]
    // Wintertijd (UTC+1).
    [InlineData("2026-12-01T12:00:00Z", "2026-12-02T02:00:00Z")]
    // Nacht van de overgang naar wintertijd (25 oktober 2026): 03:00 bestaat één keer, UTC+1.
    [InlineData("2026-10-24T22:00:00Z", "2026-10-25T02:00:00Z")]
    public void Elke_nacht_om_drie_uur_in_Loil(string now, string expected)
    {
        var next = JobSchedule.DailyAt(Loil, 3, 0)(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), next);
    }
}
