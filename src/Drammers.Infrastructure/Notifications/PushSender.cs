using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Notifications;

/// <summary>
/// Eén pushbericht naar één token. De payload bevat alleen titel, tekst en het id (geen persoonsgegevens). <c>Badge</c> is
/// het aantal ongelezen meldingen voor het rode bolletje op het app-icoon (iOS); <c>null</c> = niet wijzigen (gasten).
/// </summary>
public sealed record PushMessage(string Token, string Title, string Body, string ChannelId, bool HighPriority, IReadOnlyDictionary<string, string> Data, int? Badge = null);

/// <summary>Resultaat per bericht, in dezelfde volgorde: een ticket-id of een foutcode (bijv. <c>DeviceNotRegistered</c>).</summary>
public sealed record PushTicket(string? TicketId, string? ErrorCode);

/// <summary>Afleverstatus van een ticket; <c>null</c> als Expo nog geen receipt heeft.</summary>
public sealed record PushReceipt(bool Delivered, string? ErrorCode);

/// <summary>Verzendt pushberichten (ADR-009): Expo Push achter een eigen abstractie.</summary>
public interface IPushSender
{
    /// <summary>Maximaal aantal berichten per aanroep (Expo: 100).</summary>
    int BatchSize { get; }

    Task<IReadOnlyList<PushTicket>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, PushReceipt>> GetReceiptsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken);
}

public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary><c>Expo</c> of <c>Simulated</c> (standaard; geen echte push, wel inbox en statistiek).</summary>
    public string Provider { get; set; } = "Simulated";

    /// <summary>Naam van het Key Vault-secret met het Expo-access-token (enhanced push security).</summary>
    public string AccessTokenSecretName { get; set; } = "expo-push-access-token";

    public bool UseExpo => string.Equals(Provider, "Expo", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Expo Push API (<c>/--/api/v2/push/send</c> en <c>/getReceipts</c>) met het access token uit Key Vault. Met "enhanced
/// push security" aan is een gelekt push-token zonder dit access token onbruikbaar.
/// </summary>
internal sealed class ExpoPushSender(HttpClient http, IServiceProvider services, IOptions<PushOptions> options) : IPushSender
{
    private static readonly Uri SendUri = new("https://exp.host/--/api/v2/push/send");
    private static readonly Uri ReceiptsUri = new("https://exp.host/--/api/v2/push/getReceipts");
    private string? _accessToken;

    public int BatchSize => 100;

    public async Task<IReadOnlyList<PushTicket>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        var body = messages.Select(m => new ExpoMessage(m.Token, m.Title, m.Body, m.Data, m.ChannelId, m.HighPriority ? "high" : "default", "default", m.Badge));
        using var request = await CreateRequestAsync(SendUri, body, cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<ExpoResponse<List<ExpoTicket>>>(cancellationToken);
        var tickets = result?.Data ?? [];
        if (tickets.Count != messages.Count)
        {
            throw new HttpRequestException($"Expo gaf {tickets.Count} tickets voor {messages.Count} berichten.");
        }

        return [.. tickets.Select(t => t.Status == "ok" ? new PushTicket(t.Id, null) : new PushTicket(null, t.Details?.Error ?? "Error"))];
    }

    public async Task<IReadOnlyDictionary<string, PushReceipt>> GetReceiptsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync(ReceiptsUri, new { ids = ticketIds }, cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<ExpoResponse<Dictionary<string, ExpoTicket>>>(cancellationToken);
        return (result?.Data ?? []).ToDictionary(
            r => r.Key, r => new PushReceipt(r.Value.Status == "ok", r.Value.Status == "ok" ? null : r.Value.Details?.Error ?? "Error"));
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(Uri uri, object body, CancellationToken cancellationToken)
    {
        if (_accessToken is null)
        {
            var secrets = services.GetService<SecretClient>()
                ?? throw new InvalidOperationException("Expo Push vraagt Key Vault (Azure__KeyVaultUri) voor het access token.");
            _accessToken = (await secrets.GetSecretAsync(options.Value.AccessTokenSecretName, cancellationToken: cancellationToken)).Value.Value;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Expo Push mislukt ({(int)response.StatusCode}): {detail[..Math.Min(detail.Length, 500)]}", null, response.StatusCode);
        }
    }

    private sealed record ExpoMessage(
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body,
        [property: JsonPropertyName("data")] IReadOnlyDictionary<string, string> Data,
        [property: JsonPropertyName("channelId")] string ChannelId,
        [property: JsonPropertyName("priority")] string Priority,
        [property: JsonPropertyName("sound")] string Sound,
        [property: JsonPropertyName("badge"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Badge);

    private sealed record ExpoResponse<T>([property: JsonPropertyName("data")] T? Data);

    private sealed record ExpoTicket(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("details")] ExpoDetails? Details);

    private sealed record ExpoDetails([property: JsonPropertyName("error")] string? Error);
}

/// <summary>
/// Zonder Expo-token (lokaal, Dev tot het token er is, tests): elk bericht "slaagt" en wordt direct afgeleverd. Zo werken
/// inbox, statistiek en de worker volledig; er gaat alleen niets naar een toestel.
/// </summary>
internal sealed class SimulatedPushSender(ILogger<SimulatedPushSender> logger) : IPushSender
{
    public int BatchSize => 100;

    public Task<IReadOnlyList<PushTicket>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        logger.LogInformation("Push gesimuleerd: {Count} berichten", messages.Count);
        return Task.FromResult<IReadOnlyList<PushTicket>>([.. messages.Select(_ => new PushTicket($"sim-{Guid.NewGuid():N}", null))]);
    }

    public Task<IReadOnlyDictionary<string, PushReceipt>> GetReceiptsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, PushReceipt>>(ticketIds.ToDictionary(id => id, _ => new PushReceipt(true, null)));
}
