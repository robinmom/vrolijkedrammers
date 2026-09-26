using System.Collections.Concurrent;
using Drammers.Infrastructure.Email;

namespace Drammers.IntegrationTests.Infrastructure;

/// <summary>Houdt verstuurde e-mails bij in plaats van ze te versturen.</summary>
public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
