using System.Net;
using System.Net.Http.Json;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.EBoekhouden;

/// <summary>Instellingen <c>EBoekhouden__*</c>. Het API-token staat in Key Vault; <see cref="ApiToken"/> alleen lokaal/tests.</summary>
public sealed class EBoekhoudenOptions
{
    public const string SectionName = "EBoekhouden";

    public Uri BaseUrl { get; set; } = new("https://api.e-boekhouden.nl/");

    /// <summary>Bron-code richting e-Boekhouden (max. 10 tekens, <c>[\w_ ]</c>).</summary>
    public string Source { get; set; } = "Drammers";

    public string ApiTokenSecretName { get; set; } = "eboekhouden-api-token";

    public string? ApiToken { get; set; }

    /// <summary>ADR-010: rate limits zijn niet gedocumenteerd, dus conservatief.</summary>
    public int RequestsPerSecond { get; set; } = 5;
}

/// <summary>
/// Leden zoals de sync ze nodig heeft. Alleen deze velden worden gedeserialiseerd: IBAN, BIC, mandaat, notitie en
/// factuuradressen uit het antwoord komen dus nooit in het geheugen van de applicatie (ADR-010, dataminimalisatie).
/// </summary>
public sealed record EbMember(
    int Id,
    string? MemberNumber,
    string? Name,
    string? Salutation,
    string? Gender,
    string? Address,
    string? PostalCode,
    string? City,
    string? Country,
    string? PhoneNumber,
    string? MobilePhoneNumber,
    string? EmailAddress,
    string? FreeText1,
    string? FreeText2,
    string? FreeText3,
    string? FreeText4,
    string? FreeText5,
    string? FreeText6,
    string? FreeText7,
    string? FreeText8,
    string? FreeText9,
    string? FreeText10)
{
    public string? FreeText(string field) => field switch
    {
        "freeText1" => FreeText1,
        "freeText2" => FreeText2,
        "freeText3" => FreeText3,
        "freeText4" => FreeText4,
        "freeText5" => FreeText5,
        "freeText6" => FreeText6,
        "freeText7" => FreeText7,
        "freeText8" => FreeText8,
        "freeText9" => FreeText9,
        "freeText10" => FreeText10,
        _ => null,
    };
}

public sealed record EbMemberReference(int Id, string? MemberNumber);

/// <summary>Een geopende sessie; bij <c>DisposeAsync</c> wordt de sessie in e-Boekhouden ingetrokken.</summary>
public interface IEBoekhoudenSession : IAsyncDisposable
{
    Task<IReadOnlyList<EbMemberReference>> ListMembersAsync(CancellationToken cancellationToken);

    Task<EbMember> GetMemberAsync(int id, CancellationToken cancellationToken);
}

public interface IEBoekhoudenClient
{
    Task<IEBoekhoudenSession> OpenSessionAsync(CancellationToken cancellationToken);
}

/// <summary>Fout richting e-Boekhouden; de melding bevat geen persoonsgegevens en is geschikt voor het portal.</summary>
public sealed class EBoekhoudenException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Zonder e-Boekhouden-configuratie (alleen de database geregistreerd): elke sync faalt met een duidelijke melding.</summary>
internal sealed class UnconfiguredEBoekhoudenClient : IEBoekhoudenClient
{
    public Task<IEBoekhoudenSession> OpenSessionAsync(CancellationToken cancellationToken) =>
        throw new EBoekhoudenException("e-Boekhouden is niet geconfigureerd.");
}

/// <summary>REST-client voor e-Boekhouden API v1 (geverifieerd tegen <c>/openapi/v1.json</c>, ADR-010). Alleen lezen.</summary>
internal sealed class EBoekhoudenClient(
    HttpClient http, IOptions<EBoekhoudenOptions> options, IServiceProvider services, ILogger<EBoekhoudenClient> logger) : IEBoekhoudenClient
{
    private const int PageSize = 500;

    public async Task<IEBoekhoudenSession> OpenSessionAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var accessToken = settings.ApiToken ?? await ReadTokenFromKeyVaultAsync(settings.ApiTokenSecretName, cancellationToken);
        http.BaseAddress ??= settings.BaseUrl;

        using var response = await http.PostAsJsonAsync("v1/session", new { accessToken, source = settings.Source }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new EBoekhoudenException(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden
                ? "Aanmelden bij e-Boekhouden mislukt: controleer het API-token."
                : $"Aanmelden bij e-Boekhouden mislukt ({(int)response.StatusCode}).");
        }

        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(cancellationToken)
            ?? throw new EBoekhoudenException("e-Boekhouden gaf geen sessie terug.");
        return new Session(http, session.Token, new Throttle(settings.RequestsPerSecond), logger);
    }

    private async Task<string> ReadTokenFromKeyVaultAsync(string secretName, CancellationToken cancellationToken)
    {
        var secrets = services.GetService<SecretClient>()
            ?? throw new EBoekhoudenException("e-Boekhouden is niet geconfigureerd (geen Key Vault en geen token).");
        try
        {
            var secret = await secrets.GetSecretAsync(secretName, cancellationToken: cancellationToken);
            return secret.Value.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            throw new EBoekhoudenException($"Het e-Boekhouden-token staat nog niet in Key Vault (secret '{secretName}').", ex);
        }
    }

    private sealed record SessionResponse(string Token, int ExpiresIn);

    private sealed record MemberList(List<EbMemberReference> Items, int Count);

    private sealed class Session(HttpClient http, string token, Throttle throttle, ILogger logger) : IEBoekhoudenSession
    {
        public async Task<IReadOnlyList<EbMemberReference>> ListMembersAsync(CancellationToken cancellationToken)
        {
            var result = new List<EbMemberReference>();
            for (var offset = 0; ; offset += PageSize)
            {
                var page = await SendAsync<MemberList>($"v1/member?limit={PageSize}&offset={offset}", cancellationToken);
                result.AddRange(page.Items);
                if (page.Items.Count < PageSize || result.Count >= page.Count)
                {
                    return result;
                }
            }
        }

        public Task<EbMember> GetMemberAsync(int id, CancellationToken cancellationToken) =>
            SendAsync<EbMember>($"v1/member/{id}", cancellationToken);

        public async ValueTask DisposeAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, "v1/session");
                request.Headers.TryAddWithoutValidation("Authorization", token);
                using var _ = await http.SendAsync(request);
            }
            catch (HttpRequestException ex)
            {
                // Een sessie verloopt vanzelf; intrekken is netjes, maar niet kritisch.
                logger.LogWarning(ex, "Intrekken van de e-Boekhouden-sessie mislukt");
            }
        }

        /// <summary>GET met throttling en maximaal 4 pogingen bij 429/5xx of een netwerkfout (backoff 1, 2, 4 s).</summary>
        private async Task<T> SendAsync<T>(string path, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                await throttle.WaitAsync(cancellationToken);
                HttpResponseMessage? response = null;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, path);
                    request.Headers.TryAddWithoutValidation("Authorization", token);
                    response = await http.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                            ?? throw new EBoekhoudenException("Leeg antwoord van e-Boekhouden.");
                    }

                    var transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                    if (!transient || attempt == 4)
                    {
                        throw new EBoekhoudenException(response.StatusCode == HttpStatusCode.Unauthorized
                            ? "De sessie bij e-Boekhouden is verlopen of ongeldig."
                            : $"e-Boekhouden antwoordde met {(int)response.StatusCode}.");
                    }
                }
                catch (HttpRequestException ex) when (attempt < 4)
                {
                    logger.LogWarning(ex, "Netwerkfout richting e-Boekhouden (poging {Attempt})", attempt);
                }
                catch (HttpRequestException ex)
                {
                    throw new EBoekhoudenException("e-Boekhouden is niet bereikbaar.", ex);
                }
                finally
                {
                    response?.Dispose();
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
            }
        }
    }

    /// <summary>Maximaal <c>n</c> requests per seconde, verdeeld over de seconde.</summary>
    private sealed class Throttle(int perSecond)
    {
        private readonly TimeSpan gap = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, perSecond));
        private DateTime next = DateTime.MinValue;

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var wait = next - now;
            next = (wait > TimeSpan.Zero ? next : now) + gap;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }
        }
    }
}
