using System.Collections.Concurrent;
using Drammers.Infrastructure.Email;

namespace Drammers.IntegrationTests.Infrastructure;

/// <summary>Houdt verstuurde e-mails bij in plaats van ze te versturen.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    /// <summary>Laat de volgende verzending falen (saga-hervatting testen).</summary>
    public bool FailNextSend;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (FailNextSend)
        {
            FailNextSend = false;
            throw new HttpRequestException("ACS tijdelijk niet bereikbaar");
        }

        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
