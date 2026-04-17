using ActualChat.Audio;
using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Watches for active streams via ILiveAudioBackend computed List and multiplexes audio into a single output channel.
/// Per-author merge: when two streams overlap for the same author (e.g. reconnection), keeps the fresher stream.
/// </summary>
public sealed class LiveStreamMuxer : WorkerBase
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private readonly Channel<LiveStreamItem> _output;
    private volatile LiveStreamSettings _settings;
    private int _nextStreamIndex;

    // Per-author stream tracking for overlap detection and dedup.
    // Each author has at most one "active" stream; stale streams are cancelled.
    private readonly ConcurrentDictionary<AuthorId, AuthorStreamState> _authorStreams = new();

    public LiveStreamSettings Settings => _settings;

    private IServiceProvider Services { get; }
    private ChatId ChatId { get; }
    private ILiveAudioBackend LiveBackend => field ??= Services.GetRequiredService<ILiveAudioBackend>();
    private IStreamServer StreamServer => field ??= Services.GetRequiredService<IStreamServer>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILogger Log => field ??= Services.LogFor<LiveStreamMuxer>();

    public ChannelReader<LiveStreamItem> Output => _output.Reader;

    public LiveStreamMuxer(
        IServiceProvider services,
        ChatId chatId,
        LiveStreamSettings settings)
    {
        Services = services;
        ChatId = chatId;
        _settings = settings;
        _output = ChannelExt.Create<LiveStreamItem>(ChannelExt.UnboundedFanInOptions);
        _ = Run(); // Start immediately
    }

    public void UpdateConfig(LiveStreamSettings settings)
        => Interlocked.Exchange(ref _settings, settings);

    protected override Task OnStop()
    {
        _output.Writer.TryComplete();
        return Task.CompletedTask;
    }

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        try {
            Log.LogInformation("OnRun: Starting for chat {ChatId}", ChatId);

            var serverClock = Clocks.ServerClock;
            await serverClock.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);

            var streamTasks = new Dictionary<string, Task>();

            // Watch for streams via computed List with auto-reconnect
            while (true) {
                try {
                    Log.LogInformation("OnRun: Watching computed List for {ChatId}", ChatId);

                    while (true) {
                        var computed = await Computed.Capture(
                            () => LiveBackend.List(ChatId, cancellationToken),
                            cancellationToken).ConfigureAwait(false);
                        var currentStreams = computed.Value;

                        // Clean up completed (including failed) streams first - allows retry
                        CleanupCompletedStreams(streamTasks);

                        // Start processing any new streams
                        foreach (var streamInfo in currentStreams) {
                            if (streamTasks.ContainsKey(streamInfo.StreamId))
                                continue; // Already processing this stream

                            var streamIndex = Interlocked.Increment(ref _nextStreamIndex);
                            Log.LogDebug(
                                "Starting stream #{StreamIndex} for {AuthorId} stream #{StreamId}",
                                streamIndex, streamInfo.AuthorId, streamInfo.StreamId);
                            var streamTask = ProcessStream(streamInfo, streamIndex, cancellationToken);
                            streamTasks[streamInfo.StreamId] = streamTask;
                        }

                        // Wait for invalidation (stream registered/unregistered)
                        await computed.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception e) {
                    if (e.IsCancellationOf(cancellationToken))
                        throw;
                    Log.LogWarning(e, "OnRun: List watching failed for {ChatId}, reconnecting in {Delay}...",
                        ChatId, ReconnectDelay);
                }

                // Wait before reconnecting
                await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogError(e, "OnRun: Failed for chat {ChatId}", ChatId);
        }
        return;

        void CleanupCompletedStreams(Dictionary<string, Task> tasks) {
            var completedIds = tasks.Where(kvp => kvp.Value.IsCompleted).Select(kvp => kvp.Key).ToList();
            foreach (var id in completedIds)
                tasks.Remove(id);
        }
    }

    private async Task ProcessStream(LiveStreamInfo streamInfo, int streamIndex, CancellationToken cancellationToken)
    {
        var frameCount = 0;
        var authorId = streamInfo.AuthorId;
        var streamId = streamInfo.StreamId;

        // Register this stream and get a per-stream CTS (cancelled if a fresher stream takes over)
        var streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try {
            RegisterAuthorStream(authorId, streamId, streamInfo.BeginsAt, streamCts);

            var rpcStream = await StreamServer
                .GetAudio(streamId, TimeSpan.Zero, streamCts.Token)
                .ConfigureAwait(false);
            if (rpcStream == null) {
                Log.LogWarning("ProcessStream: Stream #{StreamId} not found", streamId);
                return;
            }

            // Emit stream start
            var startItem = new LiveStreamStart {
                StreamIndex = streamIndex,
                StreamInfo = streamInfo,
            };
            await _output.Writer.WriteAsync(startItem, streamCts.Token).ConfigureAwait(false);

            // Emit audio frames with per-author overlap filtering
            var audioStream = ((IAsyncEnumerable<AudioFrame>)rpcStream)
                .SuppressException<AudioFrame, RpcReconnectFailedException>(streamCts.Token)
                .SuppressCancellation(streamCts.Token);
            await foreach (var frame in audioStream.ConfigureAwait(false)) {
                // Early exit if superseded by a fresher stream
                if (streamCts.IsCancellationRequested)
                    break;

                if (!ShouldEmitFrame(authorId, streamId, frame))
                    continue;

                var audioFrame = new LiveAudioFrame {
                    StreamIndex = streamIndex,
                    Data = frame.Data,
                    Offset = frame.Offset,
                };
                await _output.Writer.WriteAsync(audioFrame, streamCts.Token).ConfigureAwait(false);
                frameCount++;

                UpdateLastEmittedOffset(authorId, streamId, frame.Offset);
            }

            // Emit stream end
            var endItem = new LiveStreamEnd { StreamIndex = streamIndex };
            await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
            Log.LogInformation(
                "Stream #{StreamIndex} for {AuthorId} completed, {FrameCount} frames emitted",
                streamIndex, authorId, frameCount);
        }
        catch (OperationCanceledException) when (streamCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            // Cancelled because a fresher stream replaced us — not an error
            Log.LogInformation(
                "Stream #{StreamIndex} for {AuthorId} superseded by fresher stream after {FrameCount} frames",
                streamIndex, authorId, frameCount);

            // Emit end marker so downstream cleans up
            try {
                var endItem = new LiveStreamEnd { StreamIndex = streamIndex };
                await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
            }
            catch { /* Ignore */ }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            Log.LogDebug("Stream #{StreamIndex} cancelled after {FrameCount} frames", streamIndex, frameCount);
        }
        catch (Exception e) {
            Log.LogWarning(e,
                "Error processing stream #{StreamIndex} for {AuthorId} #{StreamId}, {FrameCount} frames emitted",
                streamIndex, authorId, streamId, frameCount);

            // Still emit end marker on error
            try {
                var endItem = new LiveStreamEnd { StreamIndex = streamIndex };
                await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
            }
            catch { /* Ignore */ }
        }
        finally {
            // Remove author tracking only if we're still the active stream.
            // If a newer stream already replaced us, TryRemove returns the newer state
            // and the StreamId won't match — that's fine, we leave it.
            if (_authorStreams.TryRemove(authorId, out var removed) && removed.StreamId != streamId)
                _authorStreams.TryAdd(authorId, removed); // Put back — wasn't ours

            streamCts.Dispose();
        }
    }

    // Per-author stream management

    private void RegisterAuthorStream(AuthorId authorId, string streamId, Moment beginsAt, CancellationTokenSource streamCts)
        => _authorStreams.AddOrUpdate(
            authorId,
            _ => new AuthorStreamState(streamId, beginsAt, streamCts),
            (_, existing) => {
                if (beginsAt >= existing.BeginsAt) {
                    // New stream is fresher — cancel the old one
                    Log.LogWarning(
                        "Author {AuthorId}: stream {NewStreamId} (beginsAt={NewBeginsAt}) replaces {OldStreamId} (beginsAt={OldBeginsAt})",
                        authorId, streamId, beginsAt, existing.StreamId, existing.BeginsAt);
                    existing.Cts?.Cancel();
                    return new AuthorStreamState(streamId, beginsAt, streamCts);
                }
                // Existing stream is fresher — cancel ourselves
                Log.LogWarning(
                    "Author {AuthorId}: stream {NewStreamId} (beginsAt={NewBeginsAt}) is stale, keeping {OldStreamId} (beginsAt={OldBeginsAt})",
                    authorId, streamId, beginsAt, existing.StreamId, existing.BeginsAt);
                streamCts.Cancel();
                return existing;
            });

    private bool ShouldEmitFrame(AuthorId authorId, string streamId, AudioFrame frame)
    {
        if (!_authorStreams.TryGetValue(authorId, out var state))
            return true; // No tracking — emit (shouldn't normally happen)

        if (state.StreamId == streamId)
            return true; // We're the active stream — emit

        // We're NOT the active stream. Check for overlap.
        // If the active stream has already emitted frames beyond our offset, we're stale.
        lock (state)
            if (state.LastEmittedOffset > TimeSpan.Zero && frame.Offset <= state.LastEmittedOffset)
                return false; // Overlap — drop

        // No overlap detected yet — could be a sequential segment, emit for now.
        // But if we were supposed to be cancelled, streamCts handles that.
        return true;
    }

    private void UpdateLastEmittedOffset(AuthorId authorId, string streamId, TimeSpan offset)
    {
        if (offset < TimeSpan.Zero)
            return; // Don't track header frames

        if (!_authorStreams.TryGetValue(authorId, out var state) || state.StreamId != streamId)
            return; // Not the active stream anymore

        lock (state)
            if (offset > state.LastEmittedOffset)
                state.LastEmittedOffset = offset;
    }

    // Nested types

    private sealed class AuthorStreamState(string streamId, Moment beginsAt, CancellationTokenSource? cts)
    {
        public string StreamId { get; } = streamId;
        public Moment BeginsAt { get; } = beginsAt;
        public CancellationTokenSource? Cts { get; } = cts;
        // Guarded by lock(this) — only the active stream updates this
        public TimeSpan LastEmittedOffset { get; set; }
    }
}
