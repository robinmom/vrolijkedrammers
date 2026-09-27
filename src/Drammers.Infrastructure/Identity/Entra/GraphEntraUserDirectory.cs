using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Identity.Entra;

/// <summary>
/// Minimale Graph-client (vijf aanroepen) in plaats van de volledige Graph SDK. Aanmelden als de provisioning-app met
/// <c>User.ReadWrite.All</c> (application permission) in de External ID-tenant, met een certificaat uit Key Vault.
/// </summary>
internal sealed class GraphEntraUserDirectory(HttpClient http, GraphCredentialProvider credentials, IOptions<GraphOptions> options) : IEntraUserDirectory
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    /// <summary>Issuer van een inlog met e-mail + eenmalige code (zelf gemaakt): identity <c>federated</c>, issuer <c>mail</c>.</summary>
    public const string EmailOtpIssuer = "mail";

    public async Task<string?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        // Twee vormen: een lokaal account met wachtwoord (issuer = eigen domein) en een inlog met e-mail + code (issuer
        // "mail"). Graph kent geen "or" tussen twee identities/any-filters, dus twee zoekopdrachten.
        foreach (var issuer in new[] { EmailOtpIssuer, options.Value.IssuerDomain! })
        {
            var filter = $"identities/any(i:i/issuerAssignedId eq '{Escape(email)}' and i/issuer eq '{Escape(issuer)}')";
            using var request = await CreateRequestAsync(HttpMethod.Get, $"users?$select=id&$filter={Uri.EscapeDataString(filter)}", cancellationToken);
            using var response = await http.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<GraphList>(cancellationToken);
            if (result?.Value.FirstOrDefault()?.Id is { } id)
            {
                return id;
            }
        }

        return null;
    }

    public async Task<string?> GetSignInEmailAsync(string objectId, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(HttpMethod.Get, $"users/{Uri.EscapeDataString(objectId)}?$select=identities", cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var user = await response.Content.ReadFromJsonAsync<GraphIdentities>(cancellationToken);
        return user is null ? null : SignInEmail(user.Identities.Select(i => (i.SignInType, i.Issuer, i.IssuerAssignedId)), options.Value.IssuerDomain!);
    }

    /// <summary>
    /// Het aanmeld-e-mailadres uit de identities: bij een inlog met e-mail + code <c>federated</c>/<c>mail</c> (door de code
    /// geverifieerd), bij een lokaal account <c>emailAddress</c>/eigen domein. De UPN (<c>…@…onmicrosoft.com</c>) telt niet.
    /// </summary>
    internal static string? SignInEmail(IEnumerable<(string SignInType, string Issuer, string IssuerAssignedId)> identities, string issuerDomain) =>
        identities.FirstOrDefault(i =>
            (i.SignInType == "federated" && i.Issuer == EmailOtpIssuer) || (i.SignInType == "emailAddress" && i.Issuer == issuerDomain)).IssuerAssignedId;

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

    private sealed record GraphList([property: JsonPropertyName("value")] List<GraphUser> Value);

    private sealed record GraphUser([property: JsonPropertyName("id")] string Id);

    private sealed record GraphIdentities([property: JsonPropertyName("identities")] List<GraphIdentity> Identities);

    private sealed record GraphIdentity(
        [property: JsonPropertyName("signInType")] string SignInType,
        [property: JsonPropertyName("issuer")] string Issuer,
        [property: JsonPropertyName("issuerAssignedId")] string IssuerAssignedId);
}

/// <summary>Zonder Graph-configuratie (lokaal, tests): elke aanroep faalt met een duidelijke melding.</summary>
internal sealed class UnconfiguredEntraUserDirectory : IEntraUserDirectory
{
    private static InvalidOperationException NotConfigured() =>
        new("Microsoft Graph is niet geconfigureerd (Graph__TenantId, Graph__ClientId, Graph__IssuerDomain, Graph__CertificateName).");

    public Task<string?> FindByEmailAsync(string email, CancellationToken cancellationToken) => throw NotConfigured();

    public Task<string?> GetSignInEmailAsync(string objectId, CancellationToken cancellationToken) => throw NotConfigured();

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
