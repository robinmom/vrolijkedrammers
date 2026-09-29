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
}

public sealed record MollieNewPayment(int AmountCents, string Description, string RedirectUrl, string? WebhookUrl, Guid OrderId, string IdempotencyKey);

/// <summary>Een betaling zoals Mollie hem teruggeeft; <see cref="Status"/> is <c>open</c>, <c>pending</c>, <c>paid</c>, <c>failed</c>, <c>canceled</c> of <c>expired</c>.</summary>
public sealed record MolliePayment(string Id, string Status, string? CheckoutUrl, Guid? OrderId, int AmountCents, DateTime? PaidAt)
{
    public bool IsPaid => Status == "paid";

    /// <summary>Definitief niet betaald: de plaatsen mogen weer vrij.</summary>
    public bool IsFinallyUnpaid => Status is "failed" or "canceled" or "expired";
}

public interface IMollieClient
{
    Task<MolliePayment> CreatePaymentAsync(MollieNewPayment payment, CancellationToken cancellationToken);

    Task<MolliePayment> GetPaymentAsync(string paymentId, CancellationToken cancellationToken);
}

/// <summary>Fout richting Mollie; de gebruiker krijgt een algemene melding (geen details van Mollie).</summary>
public sealed class MollieException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Mollie Payments API v2 (docs/06 §6): de prijs komt van de server, de status wordt altijd bij Mollie opgehaald
/// (een webhook bevat alleen het id) en nieuwe betalingen krijgen een <c>Idempotency-Key</c>. Betalen met iDEAL.
/// </summary>
internal sealed class MollieClient(HttpClient http, IOptions<MollieOptions> options, IServiceProvider services, IMemoryCache cache) : IMollieClient
{
    private const string KeyCacheKey = "mollie-api-key";

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

    private sealed record Amount(string Currency, string Value);

    private sealed record Metadata(Guid? OrderId);

    private sealed record CreateBody(
        Amount Amount, string Description, string RedirectUrl, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? WebhookUrl,
        string Method, string Locale, Metadata Metadata);

    private sealed record Link(string Href);

    private sealed record Links(Link? Checkout);

    private sealed record PaymentBody(
        string Id, string Status, Amount Amount, Metadata? Metadata, DateTimeOffset? PaidAt, [property: JsonPropertyName("_links")] Links? Links);
}
