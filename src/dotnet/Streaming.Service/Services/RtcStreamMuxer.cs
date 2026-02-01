using ActualChat.Audio;
using ActualChat.Async;
using ActualChat.Chat;
using ActualChat.Rtc;

namespace ActualChat.Streaming.Services;

/// <summary>
/// Watches chat entries and multiplexes audio streams into a single output channel.
/// </summary>
public sealed class RtcStreamMuxer : IAsyncDisposable
{
    private readonly Channel<RtcItem> _output;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _watchTask;
    private volatile RtcStreamConfig _config;
    private int _nextStreamIndex;

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
        RtcStreamConfig config)
    {
        Services = services;
        Session = session;
        ChatId = chatId;
        _config = config;
        _output = Channel.CreateUnbounded<RtcItem>(ChannelExt.SingleReaderWriterUnboundedChannelOptions);
        _watchTask = Task.Run(() => WatchEntriesAsync(_cts.Token));
    }

    public void UpdateConfig(RtcStreamConfig config)
        => _config = config;

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        await _watchTask.SilentAwait(false);
        _output.Writer.TryComplete();
        _cts.Dispose();
    }

    // Private methods

    private async Task WatchEntriesAsync(CancellationToken cancellationToken)
    {
        try {
            var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
            if (chat == null || !chat.Rules.CanRead())
                return;

            var serverClock = Clocks.ServerClock;
            await serverClock.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);

            var entryReader = new ChatEntryReader(Chats, Session, ChatId, ChatEntryKind.Audio);
            var idRange = await Chats.GetIdRange(Session, ChatId, ChatEntryKind.Audio, cancellationToken)
                .ConfigureAwait(false);
            var startId = idRange.End; // Start from the latest

            var streamTasks = new Dictionary<long, Task>();
            var entries = entryReader.Observe(startId, cancellationToken);
            await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (!_config.IsListening)
                    continue;

                if (!entry.IsStreaming)
                    continue;

                if (streamTasks.ContainsKey(entry.LocalId))
                    continue; // Already processing this entry

                // Start streaming for this entry
                var streamIndex = Interlocked.Increment(ref _nextStreamIndex);
                var streamTask = ProcessStreamAsync(entry, streamIndex, cancellationToken);
                streamTasks[entry.LocalId] = streamTask;

                // Clean up completed streams
                var completedIds = streamTasks
                    .Where(kvp => kvp.Value.IsCompleted)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var id in completedIds)
                    streamTasks.Remove(id);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected
        }
        catch (Exception e) {
            Log.LogError(e, "Error watching entries for chat {ChatId}", ChatId);
        }
    }

    private async Task ProcessStreamAsync(ChatEntry entry, int streamIndex, CancellationToken cancellationToken)
    {
        try {
            var streamId = entry.StreamId;
            if (streamId.IsNullOrEmpty())
                return;

            // Get audio source
            var audioSource = await StreamClient.GetAudio(streamId, TimeSpan.Zero, cancellationToken)
                .ConfigureAwait(false);
            if (audioSource == null)
                return;

            // Emit stream start
            var startItem = new RtcStreamStart {
                StreamIndex = streamIndex,
                BeginsAt = entry.BeginsAt,
                AuthorId = entry.AuthorId,
                EntryId = entry.Id,
                Format = audioSource.Format,
            };
            await _output.Writer.WriteAsync(startItem, cancellationToken).ConfigureAwait(false);

            // Emit audio frames
            await foreach (var frame in audioSource.GetFrames(cancellationToken).ConfigureAwait(false)) {
                var audioFrame = new RtcAudioFrame {
                    StreamIndex = streamIndex,
                    Data = frame.Data,
                };
                await _output.Writer.WriteAsync(audioFrame, cancellationToken).ConfigureAwait(false);
            }

            // Emit stream end
            var endItem = new RtcStreamEnd { StreamIndex = streamIndex };
            await _output.Writer.WriteAsync(endItem, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Expected
        }
        catch (Exception e) {
            Log.LogWarning(e, "Error processing stream for entry {EntryId}", entry.Id);

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
