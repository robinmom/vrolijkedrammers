using System.Threading.Channels;

namespace Drammers.Worker.Outbox;

/// <summary>
/// Wekt de outbox-worker in hetzelfde proces zodra er een bericht is opgeslagen (B-01: API en worker in één proces).
/// Zo hoeft de worker de database niet elke paar seconden te bevragen en kan een serverless database in rust (pauze).
/// </summary>
public sealed class OutboxSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    /// <returns><c>true</c> als er een signaal kwam, <c>false</c> bij het verstrijken van de wachttijd.</returns>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout != Timeout.InfiniteTimeSpan)
        {
            timeoutSource.CancelAfter(timeout);
        }

        try
        {
            await _channel.Reader.ReadAsync(timeoutSource.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
