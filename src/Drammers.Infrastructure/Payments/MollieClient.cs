using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.Security.KeyVault.Secrets;
using Drammers.SharedKernel.Errors;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Payments;

/// <summary>
/// Instellingen <c>Mollie__*</c> (fase 19). De API-sleutel staat in Key Vault (secret <see cref="ApiKeySecretName"/>):
/// een test-sleutel (<c>test_…</c>) in Dev/Acc, de live-sleutel alleen in productie. <see cref="ApiKey"/> alleen lokaal/tests.
/// </summary>
public sealed class MollieOptions
{
    public const string SectionName = "Mollie";

    public Uri BaseUrl { get; set; } = new("https://api.mollie.com/");

    public string ApiKeySecretName { get; set; } = "mollie-api-key";

    public string? ApiKey { get; set; }

    /// <summary>
    /// Organisatie-token voor de Settlements-API (afrekeningen): dat endpoint werkt niet met een profiel-gebonden
    /// API-sleutel. Maak in Mollie een Organization access token met recht <c>settlements.read</c> en zet het in Key Vault
    /// onder <see cref="OrganizationTokenSecretName"/>. Zonder token zijn de afrekeningen simpelweg niet beschikbaar.
    /// </summary>
    public string OrganizationTokenSecretName { get; set; } = "mollie-organization-token";

    public string? OrganizationToken { get; set; }
}

public sealed record MollieNewPayment(int AmountCents, string Description, string RedirectUrl, string? WebhookUrl, Guid OrderId, string IdempotencyKey);

/// <summary>Een betaling zoals Mollie hem teruggeeft; <see cref="Status"/> is <c>open</c>, <c>pending</c>, <c>paid</c>, <c>failed</c>, <c>canceled</c> of <c>expired</c>.</summary>
public sealed record MolliePayment(string Id, string Status, string? CheckoutUrl, Guid? OrderId, int AmountCents, DateTime? PaidAt)
{
    public bool IsPaid => Status == "paid";

    /// <summary>Definitief niet betaald: de plaatsen mogen weer vrij.</summary>
    public bool IsFinallyUnpaid => Status is "failed" or "canceled" or "expired";
}

/// <summary>
/// Een Mollie-afrekening (settlement): één uitbetaling naar de bankrekening. <see cref="NetCents"/> is wat op de bank
/// binnenkomt (= <see cref="GrossCents"/> omzet min <see cref="CostsCents"/> Mollie-kosten). <see cref="Status"/> is
/// <c>open</c>, <c>pending</c>, <c>paidout</c> of <c>failed</c>.
/// </summary>
public sealed record MollieSettlement(
    string Id, string? Reference, string Status, DateTime? CreatedAt, DateTime? SettledAt, int NetCents, int GrossCents, int CostsCents);

/// <summary>Een betaling binnen een afrekening; <see cref="Id"/> is <c>tr_…</c>, te herleiden naar onze eigen inkomst.</summary>
public sealed record MollieSettlementPayment(string Id, string Status, int AmountCents, string? Description, DateTime? PaidAt);

public interface IMollieClient
{
    Task<MolliePayment> CreatePaymentAsync(MollieNewPayment payment, CancellationToken cancellationToken);

    Task<MolliePayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken);

    /// <summary>De afrekeningen (uitbetalingen) van de organisatie, nieuwste eerst.</summary>
    Task<IReadOnlyList<MollieSettlement>> ListSettlementsAsync(CancellationToken cancellationToken);

    /// <summary>Eén afrekening, of <c>null</c> als die niet bestaat.</summary>
    Task<MollieSettlement?> GetSettlementAsync(string settlementId, CancellationToken cancellationToken);

    /// <summary>De betalingen die in een afrekening zitten (gepagineerd afgehandeld).</summary>
    Task<IReadOnlyList<MollieSettlementPayment>> ListSettlementPaymentsAsync(string settlementId, CancellationToken cancellationToken);
}

/// <summary>Fout richting Mollie; de gebruiker krijgt een algemene melding (geen details van Mollie).</summary>
public sealed class MollieException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>De Settlements-API (afrekeningen) is niet beschikbaar omdat er geen organisatie-token is ingesteld.</summary>
public sealed class MollieSettlementsUnavailableException() : Exception("De Mollie-afrekeningen vereisen een organisatie-token dat nog niet is ingesteld.");

/// <summary>
/// Mollie Payments API v2 (docs/06 §6): de prijs komt van de server, de status wordt altijd bij Mollie opgehaald
/// (een webhook bevat alleen het id) en nieuwe betalingen krijgen een <c>Idempotency-Key</c>. Betalen met iDEAL.
/// </summary>
internal sealed class MollieClient(HttpClient http, IOptions<MollieOptions> options, IServiceProvider services, IMemoryCache cache) : IMollieClient
{
    private const string KeyCacheKey = "mollie-api-key";
    private const string OrgTokenCacheKey = "mollie-organization-token";

    public async Task<MolliePayment> CreatePaymentAsync(MollieNewPayment payment, CancellationToken cancellationToken)
    {
        var body = new CreateBody(
            new Amount("EUR", (payment.AmountCents / 100m).ToString("0.00", CultureInfo.InvariantCulture)),
            payment.Description, payment.RedirectUrl, payment.WebhookUrl, "ideal", "nl_NL", new Metadata(payment.OrderId));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.Value.BaseUrl, "v2/payments")) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", payment.IdempotencyKey);
        return await SendAsync(request, cancellationToken);
    }

    public async Task<MolliePayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken)
    {
        // Het id komt uit een (onbetrouwbare) webhook: alleen de vorm tr_xxx doorlaten.
        if (!paymentId.StartsWith("tr_", StringComparison.Ordinal) || paymentId.Length > 40 || !paymentId.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            throw new MollieException("Ongeldig betalings-id.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.Value.BaseUrl, $"v2/payments/{paymentId}"));
        return await SendAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<MollieSettlement>> ListSettlementsAsync(CancellationToken cancellationToken)
    {
        var token = await OrganizationTokenAsync(cancellationToken);
        // Eén pagina (max 250) is ruim voldoende: afrekeningen zijn hoogstens enkele per maand.
        var body = await SendJsonAsync<SettlementListBody>(new Uri(options.Value.BaseUrl, "v2/settlements?limit=250"), token, cancellationToken);
        return [.. (body?.Embedded?.Settlements ?? []).Select(Map)];
    }

    public async Task<MollieSettlement?> GetSettlementAsync(string settlementId, CancellationToken cancellationToken)
    {
        var token = await OrganizationTokenAsync(cancellationToken);
        var body = await SendJsonAsync<SettlementBody>(new Uri(options.Value.BaseUrl, $"v2/settlements/{ValidSettlementId(settlementId)}"), token, cancellationToken);
        return body is null ? null : Map(body);
    }

    public async Task<IReadOnlyList<MollieSettlementPayment>> ListSettlementPaymentsAsync(string settlementId, CancellationToken cancellationToken)
    {
        var token = await OrganizationTokenAsync(cancellationToken);
        var id = ValidSettlementId(settlementId);
        var payments = new List<MollieSettlementPayment>();
        Uri? next = new(options.Value.BaseUrl, $"v2/settlements/{id}/payments?limit=250");
        // Veiligheidsgrens: hoogstens 20 pagina's (5000 betalingen) om geen oneindige keten te volgen.
        for (var page = 0; next is not null && page < 20; page++)
        {
            var body = await SendJsonAsync<PaymentListBody>(next, token, cancellationToken);
            foreach (var p in body?.Embedded?.Payments ?? [])
            {
                payments.Add(new MollieSettlementPayment(p.Id, p.Status, Cents(p.Amount.Value), p.Description, p.PaidAt?.UtcDateTime));
            }

            next = body?.Links?.Next?.Href is { } href ? new Uri(href) : null;
        }

        return payments;
    }

    private static MollieSettlement Map(SettlementBody s)
    {
        var gross = 0;
        var costs = 0;
        foreach (var year in s.Periods?.Values ?? [])
        {
            foreach (var month in year.Values)
            {
                gross += (month.Revenue ?? []).Sum(r => Cents(r.AmountGross.Value));
                costs += (month.Costs ?? []).Sum(c => Cents(c.AmountGross.Value));
            }
        }

        return new MollieSettlement(s.Id, s.Reference, s.Status, s.CreatedAt?.UtcDateTime, s.SettledAt?.UtcDateTime, Cents(s.Amount.Value), gross, costs);
    }

    private static int Cents(string value) => (int)Math.Round(decimal.Parse(value, CultureInfo.InvariantCulture) * 100m);

    private static string ValidSettlementId(string settlementId)
    {
        if (!settlementId.StartsWith("stl_", StringComparison.Ordinal) || settlementId.Length > 40 || !settlementId.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            throw new MollieException("Ongeldig afrekening-id.");
        }

        return settlementId;
    }

    private async Task<T?> SendJsonAsync<T>(Uri uri, string bearer, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return default;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MollieException($"Mollie gaf status {(int)response.StatusCode}.");
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private async Task<MolliePayment> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await ApiKeyAsync(cancellationToken));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new MollieException($"Mollie gaf status {(int)response.StatusCode}.");
        }

        var p = await response.Content.ReadFromJsonAsync<PaymentBody>(cancellationToken) ?? throw new MollieException("Leeg antwoord van Mollie.");
        var cents = (int)Math.Round(decimal.Parse(p.Amount.Value, CultureInfo.InvariantCulture) * 100m);
        return new MolliePayment(p.Id, p.Status, p.Links?.Checkout?.Href, p.Metadata?.OrderId, cents, p.PaidAt?.UtcDateTime);
    }

    private async Task<string> ApiKeyAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            return options.Value.ApiKey;
        }

        if (cache.TryGetValue(KeyCacheKey, out string? cached) && cached is not null)
        {
            return cached;
        }

        var secrets = services.GetService<SecretClient>()
            ?? throw new DomainException(ErrorCodes.PaymentsNotConfigured, "Online betalen is nog niet ingesteld.", DomainErrorKind.Conflict);
        try
        {
            var secret = await secrets.GetSecretAsync(options.Value.ApiKeySecretName, cancellationToken: cancellationToken);
            // Kort bewaren: een nieuwe sleutel in Key Vault is binnen tien minuten actief.
            return cache.Set(KeyCacheKey, secret.Value.Value, TimeSpan.FromMinutes(10));
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            throw new DomainException(ErrorCodes.PaymentsNotConfigured, "Online betalen is nog niet ingesteld (de Mollie-sleutel staat nog niet in Key Vault).", DomainErrorKind.Conflict);
        }
    }

    private async Task<string> OrganizationTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.OrganizationToken))
        {
            return options.Value.OrganizationToken;
        }

        if (cache.TryGetValue(OrgTokenCacheKey, out string? cached) && cached is not null)
        {
            return cached;
        }

        // Zonder Key Vault of zonder dit secret zijn de afrekeningen simpelweg niet beschikbaar (geen harde fout).
        var secrets = services.GetService<SecretClient>() ?? throw new MollieSettlementsUnavailableException();
        try
        {
            var secret = await secrets.GetSecretAsync(options.Value.OrganizationTokenSecretName, cancellationToken: cancellationToken);
            return cache.Set(OrgTokenCacheKey, secret.Value.Value, TimeSpan.FromMinutes(10));
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            throw new MollieSettlementsUnavailableException();
        }
    }

    private sealed record Amount(string Currency, string Value);

    private sealed record Metadata(Guid? OrderId);

    private sealed record CreateBody(
        Amount Amount, string Description, string RedirectUrl, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WebhookUrl,
        string Method, string Locale, Metadata Metadata);

    private sealed record Link(string Href);

    private sealed record Links(Link? Checkout);

    private sealed record PaymentBody(
        string Id, string Status, Amount Amount, Metadata? Metadata, DateTimeOffset? PaidAt, [property: JsonPropertyName("_links")] Links? Links);

    private sealed record NextLink([property: JsonPropertyName("next")] Link? Next);

    private sealed record SettlementRevenue(Amount AmountGross);

    private sealed record SettlementPeriod(
        [property: JsonPropertyName("revenue")] IReadOnlyList<SettlementRevenue>? Revenue,
        [property: JsonPropertyName("costs")] IReadOnlyList<SettlementRevenue>? Costs);

    private sealed record SettlementBody(
        string Id, string? Reference, string Status, Amount Amount, DateTimeOffset? CreatedAt, DateTimeOffset? SettledAt,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, SettlementPeriod>>? Periods);

    private sealed record SettlementList([property: JsonPropertyName("settlements")] IReadOnlyList<SettlementBody>? Settlements);

    private sealed record SettlementListBody([property: JsonPropertyName("_embedded")] SettlementList? Embedded);

    private sealed record SettlementPaymentBody(string Id, string Status, Amount Amount, string? Description, DateTimeOffset? PaidAt);

    private sealed record PaymentList([property: JsonPropertyName("payments")] IReadOnlyList<SettlementPaymentBody>? Payments);

    private sealed record PaymentListBody(
        [property: JsonPropertyName("_embedded")] PaymentList? Embedded, [property: JsonPropertyName("_links")] NextLink? Links);
}
