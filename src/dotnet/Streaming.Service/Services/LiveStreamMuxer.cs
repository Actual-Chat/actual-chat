using ActualChat.Live;
using ActualLab.Rpc;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Watches for active streams via ILiveAudioBackend computed List and multiplexes audio into a single output channel.
/// </summary>
public sealed class LiveStreamMuxer : WorkerBase
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private readonly Channel<LiveStreamItem> _output;
    private volatile LiveStreamSettings _settings;
    private int _nextStreamIndex;

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
                            Log.LogInformation("Starting stream #{StreamIndex} for stream #{StreamId}", streamIndex, streamInfo.StreamId);
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
        try {
            var streamId = streamInfo.StreamId;
            var rpcStream = await StreamServer
                .GetAudio(streamId, TimeSpan.Zero, cancellationToken)
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
            await _output.Writer.WriteAsync(startItem, cancellationToken).ConfigureAwait(false);
            Log.LogDebug("Emitted StreamStart for stream #{StreamIndex}", streamIndex);

            // Emit audio frames
            var audioStream = ((IAsyncEnumerable<byte[]>)rpcStream)
                .SuppressException<byte[], RpcReconnectFailedException>(cancellationToken)
                .SuppressCancellation(cancellationToken);
            await foreach (var data in audioStream.ConfigureAwait(false)) {
                var audioFrame = new LiveAudioFrame {
                    StreamIndex = streamIndex,
                    Data = data,
                };
                await _output.Writer.WriteAsync(audioFrame, cancellationToken).ConfigureAwait(false);
                frameCount++;
            }

            // Emit stream end
            var endItem = new LiveStreamEnd { StreamIndex = streamIndex };
            await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Stream #{StreamIndex} completed, {FrameCount} frames emitted", streamIndex, frameCount);

            // TODO(AK): delay stream task until it consumed by all readers - 3-5s
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            Log.LogDebug("Stream #{StreamIndex} cancelled after {FrameCount} frames", streamIndex, frameCount);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Error processing stream #{StreamIndex} for stream #{StreamId}, {FrameCount} frames emitted",
                streamIndex, streamInfo.StreamId, frameCount);

            // Still emit end marker on error
            try {
                var endItem = new LiveStreamEnd { StreamIndex = streamIndex };
                await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
            }
            catch {
                // Ignore
            }
        }
    }
}
