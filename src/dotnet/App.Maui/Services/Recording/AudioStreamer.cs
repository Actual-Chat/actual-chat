using System.Buffers;
using ActualChat.UI.Blazor;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.App.Maui.Services.Recording;

internal sealed class AudioStreamer
{
    // Constants mirrored from TS (workers/audio-streamer.ts) with reasonable defaults
    private static readonly TimeSpan BufferDuration = TimeSpan.FromMilliseconds(FrameDurationMs * 10);
    private const int FrameDurationMs = 20; // Opus frame
    private const int MaxPackFrames = 20; // up to 400ms per send

    private readonly HubConnection _connection;

    private UIHub Hub { get; }
    public bool IsConnected => _connection.State == HubConnectionState.Connected;

    public AudioStreamer(UIHub hub)
    {
        Hub = hub;
        var baseUrl = hub.HostInfo.BaseUrl;
        var hubUrl = new Uri(new Uri(baseUrl, UriKind.Absolute), "/api/hub/streams").ToString();
        var builder = new HubConnectionBuilder()
            .WithUrl(hubUrl, HttpTransportType.WebSockets, options => {
                options.SkipNegotiation = true;
                options.UseStatefulReconnect = true;
            })
            .WithAutomaticReconnect(new MauiRetryPolicy())
            .ConfigureLogging(_ => { /* use hosting logging */ });

        // If MessagePack is available in project, it will be auto-registered via extension.
        _connection = builder.Build();
        _ = _connection.StartAsync(); // fire-and-forget initial start
    }

    public async Task EnsureConnected(bool quickReconnect, CancellationToken cancellationToken)
    {
        var c = _connection;
        if (c.State == HubConnectionState.Connected)
            return;

        // Ensure a connection with simple retry policy; honor quickReconnect by shortening wait
        var delay = quickReconnect ? 50 : 200;
        while (c.State != HubConnectionState.Connected) {
            if (c.State == HubConnectionState.Disconnected) {
                try {
                    await c.StartAsync(cancellationToken).ConfigureAwait(false);
                    if (c.State == HubConnectionState.Connected)
                        break;
                }
                catch {
                    // ignore; will delay and retry
                }
            }
            // Otherwise, wait and retry
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = Math.Min(delay * 2, 1000);
        }
    }

    public async Task Send(
        string sessionToken,
        ChatId chatId,
        ChatEntryId? repliedChatEntryId,
        int preSkip,
        IAsyncEnumerable<IMemoryOwner<byte>> packetStream,
        CancellationToken cancellationToken)
    {
        // clientStartOffset = seconds since epoch (double)
        double clientStartOffset = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        // Ensure connection prior to sending
        await EnsureConnected(quickReconnect: false, cancellationToken).ConfigureAwait(false);
        // Create batched async enumerable and stream to server
        var batched = packetStream
            .Buffer(BufferDuration, Hub.Clocks.CpuClock, cancellationToken)
            .SelectMany(list => {
                // Split into chunks of max size
                var batches = new List<byte[][]>();
                for (var i = 0; i < list.Count; i += MaxPackFrames) {
                    var count = Math.Min(MaxPackFrames, list.Count - i);
                    var batch = new byte[count][];
                    for (var j = 0; j < count; j++) {
                        using var owner = list[i + j];
                        batch[j] = owner.Memory.ToArray();
                    }
                    batches.Add(batch);
                }
                return batches.ToAsyncEnumerable();
            });

        await _connection.SendAsync("ProcessAudioChunks", sessionToken, chatId.Value, repliedChatEntryId?.Value, clientStartOffset, preSkip, batched, cancellationToken).ConfigureAwait(false);
    }

    private sealed class MauiRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            // Quick retries like TS: 10, 100, 500, 1000 ms, then stop
            var seq = new[] { 10, 100, 500, 1000 };
            var idx = Math.Min(retryContext.PreviousRetryCount, seq.Length - 1);
            return TimeSpan.FromMilliseconds(seq[idx]);
        }
    }
}
