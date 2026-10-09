using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Drammers.Infrastructure.Email;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Contact;

public sealed record ContactRecipient(string Label, string Address);

/// <summary>Ontvangers van het contactformulier (<c>Contact:Recipients</c>); de adressen staan nooit op de website.</summary>
public sealed class ContactOptions
{
    public const string SectionName = "Contact";

    public Dictionary<string, ContactRecipient> Recipients { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["secretariaat"] = new("Ledenadministratie", "secretaris@vrolijkedrammers.nl"),
        ["optocht"] = new("Optocht", "optocht@vrolijkedrammers.nl"),
        ["penningmeester"] = new("Kaarten en betalingen", "penningmeester@vrolijkedrammers.nl"),
        ["overig"] = new("Overige vragen", "secretaris@vrolijkedrammers.nl"),
    };

    /// <summary>Sneller ingevuld dan dit is vrijwel zeker een bot.</summary>
    public TimeSpan MinimumFillTime { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>
/// Cloudflare Turnstile. De openbare site key is een app-instelling (<c>Turnstile__SiteKey</c>, via Bicep en de
/// GitHub-variabele <c>DVD_TURNSTILE_SITE_KEY</c>); de geheime sleutel staat in Key Vault (secret
/// <c>turnstile-secret-key</c>) of, lokaal, in <c>Turnstile:SecretKey</c>. Zonder site key staat Turnstile uit en
/// beschermen alleen het verborgen veld, de invultijd en de rate limit het formulier (OQ-45).
/// </summary>
public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    public string? SiteKey { get; set; }

    public string? SecretKey { get; set; }

    public string SecretName { get; set; } = "turnstile-secret-key";

    public bool Enabled => !string.IsNullOrWhiteSpace(SiteKey);
}

public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken);
}

internal sealed partial class TurnstileVerifier(
    HttpClient http, IOptions<TurnstileOptions> options, IServiceProvider services, ILogger<TurnstileVerifier> logger) : ITurnstileVerifier
{
    private static readonly Uri Endpoint = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");
    private static string? _secretFromVault;

    private async Task<string?> SecretAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.SecretKey))
        {
            return options.Value.SecretKey;
        }

        if (_secretFromVault is not null)
        {
            return _secretFromVault;
        }

        if (services.GetService(typeof(Azure.Security.KeyVault.Secrets.SecretClient)) is not Azure.Security.KeyVault.Secrets.SecretClient secrets)
        {
            return null;
        }

        try
        {
            _secretFromVault = (await secrets.GetSecretAsync(options.Value.SecretName, cancellationToken: cancellationToken)).Value.Value;
            return _secretFromVault;
        }
        catch (Azure.RequestFailedException exception)
        {
            LogSecretMissing(logger, exception, options.Value.SecretName);
            return null;
        }
    }

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (await SecretAsync(cancellationToken) is not { } secret)
        {
            return false;
        }

        var form = new Dictionary<string, string> { ["secret"] = secret, ["response"] = token };
        if (remoteIp is not null)
        {
            form["remoteip"] = remoteIp;
        }

        try
        {
            using var response = await http.PostAsync(Endpoint, new FormUrlEncodedContent(form), cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken);
            return result?.Success == true;
        }
        catch (HttpRequestException exception)
        {
            LogUnavailable(logger, exception);
            return false;
        }
    }

    private sealed record SiteVerifyResponse([property: JsonPropertyName("success")] bool Success);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Turnstile niet bereikbaar")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Turnstile staat aan, maar het secret {SecretName} staat niet in Key Vault")]
    private static partial void LogSecretMissing(ILogger logger, Exception exception, string secretName);
}

public sealed record ContactMessageInput(
    string Recipient, string Name, string Email, string? Phone, string Message, string? Website, int? ElapsedMs, string? TurnstileToken);

/// <summary>
/// Het contactformulier van de website (fase 21i): één formulier voor alle ontvangers in plaats van e-mailadressen op de
/// site. Het bericht gaat per e-mail naar de gekozen ontvanger; antwoorden gaan via Reply-To naar de afzender. Er wordt
/// niets opgeslagen; de audit bevat alleen de ontvanger.
/// </summary>
public sealed class ContactForm(
    IOptions<ContactOptions> options, IOptions<TurnstileOptions> turnstile, ITurnstileVerifier verifier, IEmailSender email, IAuditLogger audit)
{
    /// <summary>Verstuurt het bericht. Geeft <c>false</c> als het als spam is weggegooid (de bezoeker merkt daar niets van).</summary>
    public async Task<bool> SendAsync(ContactMessageInput input, string? remoteIp, CancellationToken cancellationToken)
    {
        if (!options.Value.Recipients.TryGetValue(input.Recipient, out var recipient))
        {
            throw new DomainException(ErrorCodes.Validation, "Kies naar wie je bericht moet.");
        }

        if (turnstile.Value.Enabled && !await verifier.VerifyAsync(input.TurnstileToken, remoteIp, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "De controle of je geen robot bent is mislukt. Probeer het opnieuw.");
        }

        // Verborgen veld ingevuld of binnen een paar seconden verstuurd: een bot. Doen alsof het gelukt is.
        if (!string.IsNullOrEmpty(input.Website) || input.ElapsedMs is null || input.ElapsedMs < options.Value.MinimumFillTime.TotalMilliseconds)
        {
            await audit.WriteAsync(new AuditEntry("contact.message-dropped", "Contact", input.Recipient.ToLowerInvariant()), cancellationToken);
            return false;
        }

        var name = input.Name.Trim();
        var subject = $"Contactformulier ({recipient.Label}): {name}";
        var text = $"""
            Bericht via het contactformulier op de website.

            Naam: {name}
            E-mailadres: {input.Email.Trim()}
            Telefoon: {(string.IsNullOrWhiteSpace(input.Phone) ? "—" : input.Phone.Trim())}

            {input.Message.Trim()}

            Beantwoorden kan rechtstreeks: je antwoord gaat naar het e-mailadres van de afzender.
            """;
        var html = $"""
            <p>Bericht via het contactformulier op de website.</p>
            <p><strong>Naam:</strong> {Encode(name)}<br><strong>E-mailadres:</strong> {Encode(input.Email.Trim())}<br><strong>Telefoon:</strong> {(string.IsNullOrWhiteSpace(input.Phone) ? "—" : Encode(input.Phone.Trim()))}</p>
            <p style="white-space:pre-line">{Encode(input.Message.Trim())}</p>
            <p style="color:#666">Beantwoorden kan rechtstreeks: je antwoord gaat naar het e-mailadres van de afzender.</p>
            """;
        await email.SendAsync(new EmailMessage(recipient.Address, subject, text, html, ReplyTo: input.Email.Trim()), cancellationToken);
        await audit.WriteAsync(new AuditEntry("contact.message-sent", "Contact", input.Recipient.ToLowerInvariant()), cancellationToken);
        return true;
    }

    private static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);
}
