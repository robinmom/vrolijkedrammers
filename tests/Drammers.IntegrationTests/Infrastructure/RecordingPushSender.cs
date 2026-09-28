using System.Collections.Concurrent;
using Drammers.Infrastructure.Notifications;

namespace Drammers.IntegrationTests.Infrastructure;

/// <summary>Push in het geheugen: legt verzonden berichten vast; tokens in <see cref="Unregistered"/> geven <c>DeviceNotRegistered</c>.</summary>
public sealed class RecordingPushSender : IPushSender
{
    public ConcurrentQueue<PushMessage> Sent { get; } = new();

    public ConcurrentDictionary<string, bool> Unregistered { get; } = new();

    public int BatchSize => 2;

    public Task<IReadOnlyList<PushTicket>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        var tickets = new List<PushTicket>();
        foreach (var message in messages)
        {
            Sent.Enqueue(message);
            tickets.Add(Unregistered.ContainsKey(message.Token) ? new PushTicket(null, "DeviceNotRegistered") : new PushTicket($"t-{Guid.NewGuid():N}", null));
        }

        return Task.FromResult<IReadOnlyList<PushTicket>>(tickets);
    }

    public Task<IReadOnlyDictionary<string, PushReceipt>> GetReceiptsAsync(IReadOnlyList<string> ticketIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, PushReceipt>>(ticketIds.ToDictionary(id => id, _ => new PushReceipt(true, null)));
}
