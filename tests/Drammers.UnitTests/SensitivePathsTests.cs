using Drammers.Api.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace Drammers.UnitTests;

/// <summary>L20: geen geheime tokens uit het pad in de logs en Application Insights.</summary>
public class SensitivePathsTests
{
    [Theory]
    [InlineData("/api/v1/membership-applications/split/abc123geheim", "/api/v1/membership-applications/split/{token}")]
    [InlineData("/API/v1/membership-applications/split/abc123geheim", "/API/v1/membership-applications/split/{token}")]
    [InlineData("/mailing-voorbeeld/0123456789abcdef0123456789abcdef", "/mailing-voorbeeld/{token}")]
    [InlineData("/api/v1/membership-applications", "/api/v1/membership-applications")]
    [InlineData("/api/v1/membership-applications/split/", "/api/v1/membership-applications/split/")]
    [InlineData("/nieuws/split", "/nieuws/split")]
    [InlineData("", "")]
    public void Token_in_het_pad_wordt_vervangen_door_het_template(string path, string expected) =>
        Assert.Equal(expected, SensitivePaths.Redact(path));

    [Fact]
    public void Enricher_redigeert_RequestPath_en_Path()
    {
        var logEvent = new LogEvent(DateTimeOffset.UtcNow, LogEventLevel.Information, null,
            new MessageTemplateParser().Parse("HTTP {RequestMethod} {RequestPath}"),
            [
                new LogEventProperty("RequestMethod", new ScalarValue("GET")),
                new LogEventProperty("RequestPath", new ScalarValue("/api/v1/membership-applications/split/geheim")),
                new LogEventProperty("Path", new ScalarValue("/mailing-voorbeeld/geheim")),
            ]);

        new SensitivePathEnricher().Enrich(logEvent, null!);

        Assert.Equal("HTTP \"GET\" \"/api/v1/membership-applications/split/{token}\"", logEvent.RenderMessage());
        Assert.Equal("\"/mailing-voorbeeld/{token}\"", logEvent.Properties["Path"].ToString());
    }
}
