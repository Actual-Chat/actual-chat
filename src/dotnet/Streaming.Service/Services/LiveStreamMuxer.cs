using ActualChat.Chat;
using ActualChat.Live;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Watches for active streams via ILiveBackend and multiplexes audio into a single output channel.
/// </summary>
public sealed class LiveStreamMuxer : WorkerBase
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    private readonly Channel<LiveStreamItem> _output;
    private volatile LiveStreamSettings _settings;
    private int _nextStreamIndex;

    public LiveStreamSettings Settings => _settings;

    private IServiceProvider Services { get; }
    private Session Session { get; }
    private ChatId ChatId { get; }
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private ILiveBackend LiveBackend => field ??= Services.GetRequiredService<ILiveBackend>();
    private IStreamClient StreamClient => field ??= Services.GetRequiredService<IStreamClient>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILogger Log => field ??= Services.LogFor<LiveStreamMuxer>();

    public ChannelReader<LiveStreamItem> Output => _output.Reader;

    public LiveStreamMuxer(
        IServiceProvider services,
        Session session,
        ChatId chatId,
        LiveStreamSettings settings)
    {
        Services = services;
        Session = session;
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
            Log.LogInformation("OnRun: Starting for chat {ChatId}, session {Session}", ChatId, Session);

            var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
            if (chat?.Rules.CanRead() != true) {
                Log.LogWarning("OnRun: Cannot read chat {ChatId}, chat={Chat}, rules={Rules}",
                    ChatId, chat?.Id, chat?.Rules);
                return;
            }
            Log.LogInformation("OnRun: Chat access verified for {ChatId}", ChatId);

            var serverClock = Clocks.ServerClock;
            await serverClock.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);

            var streamTasks = new Dictionary<string, Task>(StringComparer.Ordinal);

            // Watch for streams via LiveBackend with auto-reconnect
            while (true) {
                try {
                    Log.LogInformation("OnRun: Connecting to ObserveNewStreams for {ChatId}", ChatId);
                    var streams = await LiveBackend.ObserveStreams(ChatId, cancellationToken).ConfigureAwait(false);
                    await foreach (var streamInfo in streams.ConfigureAwait(false)) {
                        Log.LogDebug("OnRun: Got stream {StreamId}", streamInfo.StreamId);

                        // Clean up completed (including failed) streams first - allows retry
                        CleanupCompletedStreams(streamTasks);

                        if (streamTasks.ContainsKey(streamInfo.StreamId))
                            continue; // Already processing this stream

                        // Start streaming
                        var streamIndex = Interlocked.Increment(ref _nextStreamIndex);
                        Log.LogInformation("Starting stream #{StreamIndex} for stream {StreamId}", streamIndex, streamInfo.StreamId);
                        var streamTask = ProcessStream(streamInfo, streamIndex, cancellationToken);
                        streamTasks[streamInfo.StreamId] = streamTask;
                    }
                    Log.LogWarning("OnRun: ObserveNewStreams completed for {ChatId}", ChatId);
                }
                catch (Exception e) {
                    if (e.IsCancellationOf(cancellationToken))
                        throw;
                    Log.LogWarning(e, "OnRun: ObserveStreams failed for {ChatId}, reconnecting in {Delay}...",
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
            var audioSource = await StreamClient
                .GetAudio(streamId, TimeSpan.Zero, cancellationToken)
                .ConfigureAwait(false);
            Log.LogDebug("Got audio source, format={Format}", audioSource.Format);

            // Emit stream start
            var startItem = new LiveStreamStart {
                StreamIndex = streamIndex,
                StreamInfo = streamInfo,
            };
            await _output.Writer.WriteAsync(startItem, cancellationToken).ConfigureAwait(false);
            Log.LogDebug("Emitted StreamStart for stream #{StreamIndex}", streamIndex);

            // Emit audio frames
            await foreach (var frame in audioSource.GetFrames(cancellationToken).ConfigureAwait(false)) {
                var audioFrame = new LiveAudioFrame {
                    StreamIndex = streamIndex,
                    Data = frame.Data,
                };
                await _output.Writer.WriteAsync(audioFrame, cancellationToken).ConfigureAwait(false);
                frameCount++;
            }

            // Emit stream end
            var endItem = new LiveStreamEnd { StreamIndex = streamIndex };
            await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Stream #{StreamIndex} completed, {FrameCount} frames emitted", streamIndex, frameCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            Log.LogDebug("Stream #{StreamIndex} cancelled after {FrameCount} frames", streamIndex, frameCount);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Error processing stream #{StreamIndex} for stream {StreamId}, {FrameCount} frames emitted",
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
