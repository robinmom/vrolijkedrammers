using System.Net;
using System.Net.Http.Headers;
using Drammers.ApiTests.Infrastructure;

namespace Drammers.ApiTests;

/// <summary>Fase 1-acceptatie (B-02): de Dev-API accepteert alleen tokens voor Dev met environmentAccess = dev.</summary>
public class EnvironmentAccessTests(DevApiFactory factory) : IClassFixture<DevApiFactory>
{
    private const string CheckUrl = "/api/v1/auth/check";

    private async Task<HttpStatusCode> CallWith(string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CheckUrl);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await factory.CreateClient().SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Zonder_token_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await CallWith(null));

    [Theory]
    [InlineData("dev")]
    [InlineData("dev,acc")]
    [InlineData("acc, DEV")]
    public async Task Token_voor_Dev_met_toegang_tot_dev_204(string access) =>
        Assert.Equal(HttpStatusCode.NoContent, await CallWith(TestTokens.Create(TestTokens.DevAudience, access)));

    [Fact]
    public async Task Token_zonder_environmentAccess_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await CallWith(TestTokens.Create(TestTokens.DevAudience, null)));

    [Fact]
    public async Task Token_met_alleen_acc_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await CallWith(TestTokens.Create(TestTokens.DevAudience, "acc")));

    [Fact]
    public async Task Token_van_de_Prod_app_registratie_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await CallWith(TestTokens.Create(TestTokens.ProdAudience, "dev")));
}

/// <summary>In Production is geen environmentAccess nodig; de audience blijft wel verplicht.</summary>
public class ProductionAccessTests(ProdApiFactory factory) : IClassFixture<ProdApiFactory>
{
    [Fact]
    public async Task Token_zonder_environmentAccess_204()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create(TestTokens.DevAudience, null));

        var response = await client.GetAsync("/api/v1/auth/check");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
