using System.Buffers;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.App.Maui.Services.Recording;

internal sealed class AudioStreamer
{
    // Constants mirrored from TS (workers/audio-streamer.ts) with reasonable defaults
    private const int FrameDurationMs = 20; // Opus frame
    private const int MaxBufferedFrames = 400; // safety cap
    private const int MinPackFrames = 2;
    private const int MaxPackFrames = 20; // up to 400ms per send

    private readonly HubConnection _connection;

    public HubConnection Connection => _connection;
    public bool IsConnected => _connection.State == HubConnectionState.Connected;

    public AudioStreamer(string baseUri)
    {
        var hubUrl = new Uri(new Uri(baseUri, UriKind.Absolute), "/api/hub/streams").ToString();
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

    public AudioStream CreateStream(string sessionToken, int preSkip, string chatId, string? repliedChatEntryId)
        => new(this, sessionToken, preSkip, chatId, repliedChatEntryId);

    private async Task SendAsync(string sessionToken, string chatId, string? repliedChatEntryId, int preSkip, ChannelReader<byte[][]> reader, CancellationToken cancellationToken)
    {
        // clientStartOffset = seconds since epoch (double)
        double clientStartOffset = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        await _connection.SendAsync("ProcessAudioChunks", sessionToken, chatId, repliedChatEntryId, clientStartOffset, preSkip, reader, cancellationToken).ConfigureAwait(false);
    }

    // Nested types
    internal sealed class AudioStream(
        AudioStreamer owner,
        string sessionToken,
        int preSkip,
        string chatId,
        string? repliedChatEntryId)
        : IAsyncDisposable
    {
        private string? _repliedChatEntryId = repliedChatEntryId;

        private readonly ConcurrentQueue<byte[]> _audioPacketQueue = new();
        private readonly SemaphoreSlim _dataAvailable = new(0);
        private volatile bool _isCompleted;
        private Task? _streamTask;
        private readonly CancellationTokenSource _cts = new();

        public void AddFrame(ReadOnlySpan<byte> frame)
        {
            if (_isCompleted || frame.IsEmpty)
                return;

            _audioPacketQueue.Enqueue(frame.ToArray());
            // Cap buffer size
            while (_audioPacketQueue.Count > MaxBufferedFrames && _audioPacketQueue.TryDequeue(out _)) {
                // drop oldest frames on overflow
            }
            _dataAvailable.Release();
        }

        public void Complete()
        {
            _isCompleted = true;
            _dataAvailable.Release();
        }

        public void StartStreaming()
            => _streamTask ??= Task.Run(StreamLoopAsync);

        private async Task StreamLoopAsync()
        {
            // Create channel of byte[][] packets
            var channel = Channel.CreateUnbounded<byte[][]>(new UnboundedChannelOptions {
                SingleReader = true,
                SingleWriter = true
            });

            // Ensure connection prior to sending
            await owner.EnsureConnected(quickReconnect: false, _cts.Token).ConfigureAwait(false);
            // Fire-and-forget sending task; if it throws, we'll just stop streaming
            var sendTask = owner.SendAsync(sessionToken, chatId, _repliedChatEntryId, preSkip, channel.Reader, _cts.Token);
            _repliedChatEntryId = null; // avoid resending replied id on retries

            try {
                // batch frames
                var batch = new List<byte[]>(MaxPackFrames);
                while (!_cts.IsCancellationRequested) {
                    // Wait for data if needed
                    if (batch.Count == 0 && _audioPacketQueue.IsEmpty) {
                        if (_isCompleted)
                            break;

                        await _dataAvailable.WaitAsync(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
                    }

                    // Fill batch
                    while (batch.Count < MaxPackFrames && _audioPacketQueue.TryDequeue(out var audioPacket))
                        batch.Add(audioPacket);

                    if (batch.Count == 0)
                        continue;

                    // Send packet
                    var packetBatch = batch.ToArray();
                    await channel.Writer.WriteAsync(packetBatch, _cts.Token).ConfigureAwait(false);
                    batch.Clear();

                    // Pacing to avoid overlaps similar to TS
                    var delay = packetBatch.Length * FrameDurationMs / 2; // MAX_SPEED ~2
                    if (delay > 0)
                        await Task.Delay(delay, _cts.Token).ConfigureAwait(false);

                    if (_isCompleted && _audioPacketQueue.IsEmpty)
                        break;
                }
            }
            catch { /* ignore */ }
            finally {
                channel.Writer.TryComplete();
                try { await sendTask.ConfigureAwait(false); }
                catch { /* ignore */ }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            if (_streamTask != null)
                try { await _streamTask.ConfigureAwait(false); }
                catch { /* ignore */ }
            _cts.Dispose();
        }
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
