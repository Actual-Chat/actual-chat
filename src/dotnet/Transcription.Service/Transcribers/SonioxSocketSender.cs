using System.Net.WebSockets;

namespace ActualChat.Transcription;

// ClientWebSocket allows just one send at a time, and the keepalive loop sends
// concurrently with the audio push.

internal sealed class SonioxSocketSender(ClientWebSocket webSocket, MomentClock clock) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public Moment LastSendAt { get; private set; } = clock.Now;

    public void Dispose()
        => _lock.Dispose();

    public async Task Send(
        ReadOnlyMemory<byte> data,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await webSocket.SendAsync(data, messageType, true, cancellationToken).ConfigureAwait(false);
            LastSendAt = clock.Now;
        }
        finally {
            _lock.Release();
        }
    }
}
