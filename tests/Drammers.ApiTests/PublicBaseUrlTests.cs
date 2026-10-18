using Drammers.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Drammers.ApiTests;

/// <summary>L3: links in mails en betaallinks nooit op basis van de Host-header buiten Development en de tests.</summary>
public class PublicBaseUrlTests
{
    private static HttpRequest Request(string environment, string? publicBaseUrl)
    {
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [PublicBaseUrl.ConfigurationKey] = publicBaseUrl })
                .Build())
            .AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment })
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("aanvaller.example");
        return context.Request;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Dev")]
    [InlineData("Development")]
    public void Het_geconfigureerde_adres_wint_van_de_Host_header(string environment) =>
        Assert.Equal("https://www.vrolijkedrammers.nl", Request(environment, "https://www.vrolijkedrammers.nl/").PublicBaseUrlFor());

    [Fact]
    public void Zonder_configuratie_lokaal_terugval_op_het_verzoek() =>
        Assert.Equal("https://aanvaller.example", Request("Development", null).PublicBaseUrlFor());

    [Theory]
    [InlineData("Production")]
    [InlineData("Dev")]
    public void Zonder_configuratie_in_Azure_een_fout_in_plaats_van_de_Host_header(string environment) =>
        Assert.Throws<InvalidOperationException>(() => Request(environment, "").PublicBaseUrlFor());
}
