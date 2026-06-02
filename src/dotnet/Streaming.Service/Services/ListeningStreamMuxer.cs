using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Watches for active streams via ILiveAudioBackend computed List and multiplexes audio into a single output channel.
/// Per-author merge: when two streams overlap for the same author (e.g. reconnection), keeps the fresher stream.
/// </summary>
public sealed class ListeningStreamMuxer : WorkerBase
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan EvictionDelay = Constants.Audio.MaxRealtimeStreamDrift + TimeSpan.FromSeconds(1);

    private readonly Channel<MuxedStreamItem> _output;
    private readonly ConcurrentDictionary<string, StreamEntry> _streamById = new();
    private readonly ConcurrentDictionary<AuthorId, StreamEntry> _streamByAuthor = new();
    private int _nextStreamIndex;

    private IServiceProvider Services { get; }
    private ChatId ChatId { get; }
    private ILiveAudioStreams LiveAudioStreams => field ??= Services.GetRequiredService<ILiveAudioStreams>();
    private ILiveAudioBackend LiveAudioBackend => field ??= Services.GetRequiredService<ILiveAudioBackend>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private MomentClock SystemClock => Clocks.SystemClock;
    private ILogger Log => field ??= Services.LogFor<ListeningStreamMuxer>();

    public ChannelReader<MuxedStreamItem> Output => _output.Reader;

    public ListeningStreamMuxer(IServiceProvider services, ChatId chatId)
    {
        Services = services;
        ChatId = chatId;
        _output = ChannelExt.Create<MuxedStreamItem>(ChannelExt.UnboundedFanInOptions);
        _ = Run(); // Start immediately
    }

    protected override Task OnStop()
    {
        _output.Writer.TryComplete();
        return Task.CompletedTask;
    }

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        // Two tricky races handled here:
        // 1) A stream's playback ended, but it's still listed as active — the
        //    producer's LiveAudioBackend.Unregister runs after its audio blob save,
        //    which can take seconds for long messages. Fixed by two things:
        //    coarse stale-audio trimming in ProcessStream (so a restart replays
        //    at most a short tail) and the EvictionDelay on pruning below (so
        //    the common case doesn't restart in the first place).
        // 2) One author pushes several streams effectively at once — typically
        //    an offline backlog flushed on reconnect. They aren't ordered by
        //    length, and we have no duration until each stream ends, so we
        //    keep only the latest by BeginsAt (see TryRegister) and cancel
        //    the rest via their StopTokenSource; superseded ProcessStream
        //    calls exit their audio loop on the next OCE.
        try {
            Log.LogInformation("OnRun: Starting for chat {ChatId}", ChatId);

            // Watch for streams via computed List with auto-reconnect
            while (true) {
                try {
                    Log.LogInformation("OnRun: Watching computed List for {ChatId}", ChatId);

                    while (true) {
                        var computed = await Computed.Capture(
                            () => LiveAudioBackend.List(ChatId, cancellationToken),
                            cancellationToken).ConfigureAwait(false);
                        var currentStreams = computed.Value;

                        // Start processing any new streams
                        foreach (var streamInfo in currentStreams) {
                            if (_streamById.ContainsKey(streamInfo.StreamId))
                                continue; // Already processing this stream

                            var streamEntry = new StreamEntry(
                                Interlocked.Increment(ref _nextStreamIndex),
                                streamInfo,
                                cancellationToken.CreateLinkedTokenSource());
                            Log.LogDebug(
                                "Starting stream #{StreamIndex} for {AuthorId} stream #{StreamId}",
                                streamEntry.Index, streamInfo.AuthorId, streamInfo.StreamId);
                            _ = ProcessStream(streamEntry, cancellationToken);
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
    }

    private async Task ProcessStream(StreamEntry streamEntry, CancellationToken cancellationToken)
    {
        var (streamIndex, streamInfo, streamStopTokenSource) = streamEntry;
        var authorId = streamInfo.AuthorId;
        var streamId = streamInfo.StreamId;
        var streamStopToken = streamStopTokenSource.Token;
        var frameCount = 0;
        try {
            if (!TryRegister(streamEntry))
                return; // See `finally` block below

            // Coarse server-side stale-audio trim. This keeps reconnect/backlog
            // bursts and late joins from streaming long-past speech just so the
            // client can drop it. Fine A/V alignment still belongs to the client
            // audio buffer; this only caps how much old audio we send.
            var skipTo = SystemClock.Now - streamInfo.BeginsAt;
            skipTo = (skipTo - Constants.Audio.MaxRealtimeStreamDrift).Positive();
            var rpcStream = await LiveAudioStreams
                .GetStream(Session.Default, streamId, skipTo, streamStopToken)
                .ConfigureAwait(false);
            if (rpcStream == null) {
                Log.LogWarning("ProcessStream: Stream #{StreamId} not found", streamId);
                return;
            }

            var audioStream = rpcStream.SuppressExceptions(e => e is RpcReconnectFailedException, streamStopToken);
            await foreach (var frame in audioStream.ConfigureAwait(false)) {
                if (frameCount == 0) {
                    var startItem = new MuxedAudioStreamStart() {
                        StreamIndex = streamIndex,
                        StreamInfo = streamInfo,
                    };
                    await _output.Writer.WriteAsync(startItem, streamStopToken).ConfigureAwait(false);
                }
                var audioFrame = new MuxedAudioFrame {
                    StreamIndex = streamIndex,
                    Data = frame.Data,
                    Offset = frame.Offset,
                };
                frameCount++;
                await _output.Writer.WriteAsync(audioFrame, streamStopToken).ConfigureAwait(false);
            }
            Log.LogInformation(
                "Stream #{StreamIndex} for {AuthorId} completed, {FrameCount} frames emitted",
                streamIndex, authorId, frameCount);
        }
        catch (OperationCanceledException)
            when (streamStopToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
            // A fresher stream replaced us — not an error
            Log.LogInformation(
                "Stream #{StreamIndex} for {AuthorId} superseded by fresher stream after {FrameCount} frames",
                streamIndex, authorId, frameCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            Log.LogDebug("Stream #{StreamIndex} cancelled after {FrameCount} frames", streamIndex, frameCount);
        }
        catch (Exception e) {
            Log.LogWarning(e,
                "Error processing stream #{StreamIndex} for {AuthorId} #{StreamId}, {FrameCount} frames emitted",
                streamIndex, authorId, streamId, frameCount);
        }
        finally {
            streamStopTokenSource.CancelAndDisposeSilently();
            await EmitEndSafe().ConfigureAwait(false);

            // LiveAudioBackend.List may still enlist the same stream, so this delay
            // prevents duplicates of the same stream to be processed
            await Task.Delay(EvictionDelay, CancellationToken.None).ConfigureAwait(false);

            // Unregister the stream
            _streamById.TryRemove(streamId, streamEntry);
            _streamByAuthor.TryRemove(authorId, streamEntry);
        }
        return;

        async ValueTask EmitEndSafe() {
            if (frameCount == 0 || cancellationToken.IsCancellationRequested)
                return;

            try {
                var endItem = new MuxedAudioStreamEnd { StreamIndex = streamIndex };
                await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
            }
            catch { /* Ignore */ }
        }
    }

    // Per-author stream management

    private bool TryRegister(StreamEntry entry)
    {
        _streamById[entry.StreamId] = entry;
        return ReferenceEquals(entry, _streamByAuthor.AddOrUpdate(
            entry.AuthorId,
            _ => entry,
            (_, existing) => {
                if (entry.StreamId != existing.StreamId && entry.BeginsAt > existing.BeginsAt) {
                    // New stream is fresher — cancel the old one
                    Log.LogInformation(
                        "Author {AuthorId}: stream {NewStreamId} (beginsAt={NewBeginsAt}) replaces {OldStreamId} (beginsAt={OldBeginsAt})",
                        entry.AuthorId, entry.StreamId, entry.BeginsAt, existing.StreamId, existing.BeginsAt);
                    try { existing.StopTokenSource.CancelAndDisposeSilently(); }
                    catch (ObjectDisposedException) { }
                    return entry;
                }

                // It's either the same stream or existing stream is fresher — cancel ourselves
                Log.LogInformation(
                    "Author {AuthorId}: stream {NewStreamId} (beginsAt={NewBeginsAt}) is stale, keeping {OldStreamId} (beginsAt={OldBeginsAt})",
                    entry.AuthorId, entry.StreamId, entry.BeginsAt, existing.StreamId, existing.BeginsAt);
                entry.StopTokenSource.CancelAndDisposeSilently();
                return existing;
            }));
    }

    // Nested types

    private sealed record StreamEntry(
        int Index,
        LiveAudioStreamInfo StreamInfo,
        CancellationTokenSource StopTokenSource)
    {
        public string StreamId => StreamInfo.StreamId;
        public AuthorId AuthorId => StreamInfo.AuthorId;
        public Moment BeginsAt => StreamInfo.BeginsAt;
    }
}
