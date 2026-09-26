using Azure;
using Azure.Communication.Email;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Email;

/// <summary>Een e-mail met platte tekst en HTML (docs/06: geen persoonsgegevens in logs).</summary>
public sealed record EmailMessage(string To, string Subject, string PlainText, string Html);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Instellingen <c>Email__*</c>, gezet door Bicep (Azure Communication Services, domein van Azure).</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public Uri? Endpoint { get; set; }

    /// <summary>Bijv. <c>xxxx.azurecomm.net</c>; de afzender wordt <c>DoNotReply@</c> dit domein.</summary>
    public string? SenderDomain { get; set; }

    public bool IsConfigured => Endpoint is not null && !string.IsNullOrWhiteSpace(SenderDomain);
}

/// <summary>Verstuurt via Azure Communication Services Email met de managed identity van de API.</summary>
internal sealed class AcsEmailSender(IOptions<EmailOptions> options, TokenCredential credential) : IEmailSender
{
    private readonly EmailClient _client = new(options.Value.Endpoint!, credential);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var content = new EmailContent(message.Subject) { PlainText = message.PlainText, Html = message.Html };
        var email = new Azure.Communication.Email.EmailMessage($"DoNotReply@{options.Value.SenderDomain}", message.To, content);
        // WaitUntil.Started: ACS neemt het bericht aan en bezorgt het zelf; een fout bij aannemen gooit hier.
        await _client.SendAsync(WaitUntil.Started, email, cancellationToken);
    }
}

/// <summary>Zonder ACS (lokaal): schrijft alleen het onderwerp naar de log, niet het adres of de inhoud.</summary>
internal sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogNotSent(logger, message.Subject);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "E-mail niet verstuurd (geen Email__Endpoint): {Subject}")]
    private static partial void LogNotSent(ILogger logger, string subject);
}
