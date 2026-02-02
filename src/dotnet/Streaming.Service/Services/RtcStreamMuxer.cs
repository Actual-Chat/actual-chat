using ActualChat.Chat;
using ActualChat.Rtc;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Watches chat entries and multiplexes audio streams into a single output channel.
/// </summary>
public sealed class RtcStreamMuxer : WorkerBase
{
    private readonly Channel<RtcItem> _output;
    private volatile RtcStreamingSettings _settings;
    private int _nextStreamIndex;

    public RtcStreamingSettings Settings => _settings;

    private IServiceProvider Services { get; }
    private Session Session { get; }
    private ChatId ChatId { get; }
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    private IStreamClient StreamClient => field ??= Services.GetRequiredService<IStreamClient>();
    private MomentClockSet Clocks => field ??= Services.Clocks();
    private ILogger Log => field ??= Services.LogFor<RtcStreamMuxer>();

    public ChannelReader<RtcItem> Output => _output.Reader;

    public RtcStreamMuxer(
        IServiceProvider services,
        Session session,
        ChatId chatId,
        RtcStreamingSettings settings)
    {
        Services = services;
        Session = session;
        ChatId = chatId;
        _settings = settings;
        _output = ChannelExt.Create<RtcItem>(ChannelExt.SingleReaderWriterUnboundedChannelOptions);
        _ = Run(); // Start immediately
    }

    public void UpdateConfig(RtcStreamingSettings settings)
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
            Log.LogInformation("Starting for chat {ChatId}, session {Session}", ChatId, Session);

            var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
            if (chat?.Rules.CanRead() != true) {
                Log.LogWarning("Cannot read chat {ChatId}, chat={Chat}", ChatId, chat?.Id);
                return;
            }

            var serverClock = Clocks.ServerClock;
            await serverClock.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);

            var entryReader = new ChatEntryReader(Chats, Session, ChatId, ChatEntryKind.Audio);
            var idRange = await Chats.GetIdRange(Session, ChatId, ChatEntryKind.Audio, cancellationToken)
                .ConfigureAwait(false);
            var startId = idRange.End; // Start from the latest

            Log.LogInformation("Observing entries from {StartId}", startId);

            var streamTasks = new Dictionary<long, Task>();
            var entries = entryReader.Observe(startId, cancellationToken);
            await foreach (var entry in entries.ConfigureAwait(false)) {
                if (!entry.IsStreaming)
                    continue;
                if (streamTasks.ContainsKey(entry.LocalId))
                    continue; // Already processing this entry

                // Clean up completed streams
                CleanupCompletedStreams(streamTasks);

                // Start streaming for this entry
                var streamIndex = Interlocked.Increment(ref _nextStreamIndex);
                Log.LogInformation("Starting stream #{StreamIndex} for entry {EntryId}", streamIndex, entry.Id);
                var streamTask = ProcessStream(entry, streamIndex, cancellationToken);
                streamTasks[entry.LocalId] = streamTask;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected
        }
        catch (Exception e) {
            Log.LogError(e, "Error watching entries for chat {ChatId}", ChatId);
        }
        return;

        void CleanupCompletedStreams(Dictionary<long, Task> streamTasks) {
            var completedIds = streamTasks.Where(kvp => kvp.Value.IsCompleted).ToList();
            foreach (var (entryLid, _) in completedIds)
                streamTasks.Remove(entryLid);
        }
    }

    private async Task ProcessStream(ChatEntry entry, int streamIndex, CancellationToken cancellationToken)
    {
        var frameCount = 0;
        try {
            var streamId = entry.StreamId;
            var audioSource = await StreamClient
                .GetAudio(streamId, TimeSpan.Zero, cancellationToken)
                .ConfigureAwait(false);
            Log.LogDebug("Got audio source, format={Format}", audioSource.Format);

            // Emit stream start
            var startItem = new RtcStreamStart {
                StreamIndex = streamIndex,
                BeginsAt = entry.BeginsAt,
                AuthorId = entry.AuthorId,
                EntryId = entry.Id,
                Format = audioSource.Format,
            };
            await _output.Writer.WriteAsync(startItem, cancellationToken).ConfigureAwait(false);
            Log.LogDebug("Emitted StreamStart for stream #{StreamIndex}", streamIndex);

            // Emit audio frames
            await foreach (var frame in audioSource.GetFrames(cancellationToken).ConfigureAwait(false)) {
                var audioFrame = new RtcAudioFrame {
                    StreamIndex = streamIndex,
                    Data = frame.Data,
                };
                await _output.Writer.WriteAsync(audioFrame, cancellationToken).ConfigureAwait(false);
                frameCount++;
            }

            // Emit stream end
            var endItem = new RtcStreamEnd { StreamIndex = streamIndex };
            await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Stream #{StreamIndex} completed, {FrameCount} frames emitted", streamIndex, frameCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            Log.LogDebug("Stream #{StreamIndex} cancelled after {FrameCount} frames", streamIndex, frameCount);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Error processing stream #{StreamIndex} for entry {EntryId}, {FrameCount} frames emitted",
                streamIndex, entry.Id, frameCount);

            // Still emit end marker on error
            try {
                var endItem = new RtcStreamEnd { StreamIndex = streamIndex };
                await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
            }
            catch {
                // Ignore
            }
        }
    }
}
