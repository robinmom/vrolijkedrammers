using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Identity.Entra;

/// <summary>
/// Minimale Graph-client (vier aanroepen) in plaats van de volledige Graph SDK. Aanmelden als de provisioning-app met
/// <c>User.ReadWrite.All</c> (application permission) in de External ID-tenant, met een certificaat uit Key Vault.
/// </summary>
internal sealed class GraphEntraUserDirectory(HttpClient http, GraphCredentialProvider credentials, IOptions<GraphOptions> options) : IEntraUserDirectory
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    public async Task<string?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var issuer = options.Value.IssuerDomain;
        var filter = $"identities/any(i:i/issuerAssignedId eq '{Escape(email)}' and i/issuer eq '{Escape(issuer!)}')";
        using var request = await CreateRequestAsync(HttpMethod.Get, $"users?$select=id&$filter={Uri.EscapeDataString(filter)}", cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<GraphList>(cancellationToken);
        return result?.Value.FirstOrDefault()?.Id;
    }

    public async Task<string> CreateAsync(string email, string displayName, CancellationToken cancellationToken)
    {
        // Wachtwoord is verplicht bij aanmaken, maar wordt nooit gebruikt: de user flow kent alleen e-mail + eenmalige code.
        var body = new
        {
            displayName,
            accountEnabled = true,
            mail = email,
            identities = new[] { new { signInType = "emailAddress", issuer = options.Value.IssuerDomain, issuerAssignedId = email } },
            passwordProfile = new { password = RandomPassword(), forceChangePasswordNextSignIn = false },
            passwordPolicies = "DisablePasswordExpiration",
        };
        using var request = await CreateRequestAsync(HttpMethod.Post, "users", cancellationToken);
        request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var created = await response.Content.ReadFromJsonAsync<GraphUser>(cancellationToken);
        return created?.Id ?? throw new InvalidOperationException("Graph gaf geen id terug voor het nieuwe account");
    }

    public async Task SetAccountEnabledAsync(string objectId, bool enabled, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Patch, $"users/{Uri.EscapeDataString(objectId)}", cancellationToken);
        request.Content = JsonContent.Create(new { accountEnabled = enabled });
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RevokeSessionsAsync(string objectId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Post, $"users/{Uri.EscapeDataString(objectId)}/revokeSignInSessions", cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteAsync(string objectId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Delete, $"users/{Uri.EscapeDataString(objectId)}", cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        var credential = await credentials.GetAsync(cancellationToken);
        var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);
        var request = new HttpRequestMessage(method, new Uri(new Uri("https://graph.microsoft.com/v1.0/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Graph-aanroep mislukt ({(int)response.StatusCode}): {detail[..Math.Min(detail.Length, 500)]}", null, response.StatusCode);
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string RandomPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) + "aA1!";

    private sealed record GraphList([property: JsonPropertyName("value")] List<GraphUser> Value);

    private sealed record GraphUser([property: JsonPropertyName("id")] string Id);
}

/// <summary>Zonder Graph-configuratie (lokaal, tests): elke aanroep faalt met een duidelijke melding.</summary>
internal sealed class UnconfiguredEntraUserDirectory : IEntraUserDirectory
{
    private static InvalidOperationException NotConfigured() =>
        new("Microsoft Graph is niet geconfigureerd (Graph__TenantId, Graph__ClientId, Graph__IssuerDomain, Graph__CertificateName).");

    public Task<string?> FindByEmailAsync(string email, CancellationToken cancellationToken) => throw NotConfigured();

    public Task<string> CreateAsync(string email, string displayName, CancellationToken cancellationToken) => throw NotConfigured();

    public Task SetAccountEnabledAsync(string objectId, bool enabled, CancellationToken cancellationToken) => throw NotConfigured();

    public Task RevokeSessionsAsync(string objectId, CancellationToken cancellationToken) => throw NotConfigured();

    public Task DeleteAsync(string objectId, CancellationToken cancellationToken) => throw NotConfigured();
}

/// <summary>
/// Credential voor Graph in de External ID-tenant: het certificaat <c>graph-provisioning</c> uit Key Vault, gelezen met
/// de managed identity van de API. Na een vernieuwing door Key Vault pakt een herstart de nieuwe versie op.
/// </summary>
internal sealed class GraphCredentialProvider(IOptions<GraphOptions> options, IServiceProvider services)
{
    private TokenCredential? _credential;

    public async Task<TokenCredential> GetAsync(CancellationToken cancellationToken)
    {
        if (_credential is not null)
        {
            return _credential;
        }

        var graph = options.Value;
        var secrets = services.GetRequiredService<Azure.Security.KeyVault.Secrets.SecretClient>();
        var secret = await secrets.GetSecretAsync(graph.CertificateName, cancellationToken: cancellationToken);
        var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(
            Convert.FromBase64String(secret.Value.Value), password: null);
        _credential = new Azure.Identity.ClientCertificateCredential(graph.TenantId, graph.ClientId, certificate);
        return _credential;
    }
}
