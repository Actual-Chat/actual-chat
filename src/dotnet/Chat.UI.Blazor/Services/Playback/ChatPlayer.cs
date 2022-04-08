using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.Messaging;

namespace ActualChat.Chat.UI.Blazor.Services;

public enum ChatPlayerKind { Realtime, Historical }

public abstract class ChatPlayer : ProcessorBase
{
    /// <summary>
    /// Once enqueued, playback loop continues, so the larger is this duration,
    /// the higher is the chance to enqueue the next entry on time.
    /// </summary>
    protected static readonly TimeSpan EnqueueAheadDuration = TimeSpan.FromSeconds(1);
    protected static readonly TimeSpan InfDuration = 2 * Constants.Chat.MaxEntryDuration;
    protected static readonly TimeSpan MaxPlayStartTime = TimeSpan.FromSeconds(3);

    private volatile CancellationTokenSource? _playTokenSource;
    private volatile Task? _whenPlaying = null;

    protected ILogger Log { get; }
    protected MomentClockSet Clocks { get; }
    protected IServiceProvider Services { get; }
    protected AudioDownloader AudioDownloader { get; }
    protected IChatMediaResolver ChatMediaResolver { get; }
    protected IAudioStreamer AudioStreamer { get; }
    protected IChatAuthors ChatAuthors { get; }
    protected IChats Chats { get; }

    public Session Session { get; }
    public Symbol ChatId { get; }
    public ChatPlayerKind PlayerKind { get; protected init; }
    public Playback Playback { get; }
    public Task? WhenPlaying => _whenPlaying;

    protected ChatPlayer(Session session, Symbol chatId, IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        Clocks = services.Clocks();

        ChatId = chatId;
        Session = session;

        Playback = services.GetRequiredService<IPlaybackFactory>().Create();
        AudioDownloader = services.GetRequiredService<AudioDownloader>();
        ChatMediaResolver = services.GetRequiredService<IChatMediaResolver>();
        AudioStreamer = services.GetRequiredService<IAudioStreamer>();
        ChatAuthors = services.GetRequiredService<IChatAuthors>();
        Chats = services.GetRequiredService<IChats>();
    }

    protected override async Task DisposeAsyncCore()
    {
        try {
            await Stop().ConfigureAwait(false);
        }
        catch {
            // Intended
        }
        await Playback.DisposeAsync().ConfigureAwait(false);
    }

    public async Task<Task> Play(Moment startAt, CancellationToken cancellationToken)
    {
        this.ThrowIfDisposedOrDisposing();
        CancellationTokenSource playTokenSource;
        CancellationToken playToken;

        var spinWait = new SpinWait();
        while (true) {
            await Stop().ConfigureAwait(false);
            lock (Lock)
                if (_playTokenSource == null) {
                    _playTokenSource = playTokenSource = cancellationToken.LinkWith(StopToken);
                    playToken = playTokenSource.Token;
                    break;
                }
            // Some other Play has already started in between Stop() & lock (Lock)
            spinWait.SpinOnce();
        }

        var whenPlaying = BackgroundTask.Run(async () => {
            try {
                await PlayInternal(startAt, playToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, "Play failed for chat #{ChatId}", ChatId);
            }
            finally {
                lock (Lock) {
                    playTokenSource = _playTokenSource;
                    _playTokenSource = null;
                    _whenPlaying = null;
                }
                playTokenSource.CancelAndDisposeSilently();
            }
        }, CancellationToken.None);
        lock (Lock)
            _whenPlaying = whenPlaying;

        // We want to return from this method only when IsPlayingState.Value becomes true
        var isPlayingTask = Playback.IsPlayingState.When(x => x, playToken);
        await Task.WhenAny(isPlayingTask, whenPlaying).ConfigureAwait(false);
        return whenPlaying;
    }

    public Task Stop()
    {
        CancellationTokenSource? playTokenSource;
        lock (Lock) {
            playTokenSource = _playTokenSource;
            if (playTokenSource == null)
                return Task.CompletedTask;
            _playTokenSource = null;
        }
        playTokenSource.CancelAndDisposeSilently();
        var stopProcess = Playback.Stop(CancellationToken.None);
        return stopProcess.WhenCompleted;
    }

    // Protected methods

    protected abstract Task PlayInternal(Moment startAt, CancellationToken cancellationToken);

    protected async Task<IMessageProcess<PlayTrackCommand>> EnqueueEntry(
            Moment playAt,
            ChatEntry audioEntry,
            TimeSpan skipTo,
            CancellationToken cancellationToken)
    {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (audioEntry.Type != ChatEntryType.Audio)
                throw new NotSupportedException($"The entry's Type must be {ChatEntryType.Audio}.");
            if (audioEntry.Duration is { } duration && skipTo.TotalSeconds > duration)
                return PlayTrackCommand.PlayNothingProcess;
            return await (audioEntry.IsStreaming
                ? EnqueueStreamingEntry(playAt, audioEntry, skipTo, cancellationToken)
                : EnqueueNonStreamingEntry(playAt, audioEntry, skipTo, cancellationToken)
                ).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e,
                "Error playing audio entry; chat #{ChatId}, entry #{AudioEntryId}, stream #{StreamId}",
                audioEntry.ChatId,
                audioEntry.Id,
                audioEntry.StreamId);
            throw;
        }
    }

    // Private methods

    private async Task<IMessageProcess<PlayTrackCommand>> EnqueueStreamingEntry(
        Moment playAt,
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var audio = await AudioStreamer
            .GetAudio(audioEntry.StreamId, skipTo, cancellationToken)
            .ConfigureAwait(false);
        var trackInfo = new ChatAudioTrackInfo(audioEntry) {
            RecordedAt = audioEntry.BeginsAt + skipTo,
            ClientSideRecordedAt = (audioEntry.ClientSideBeginsAt ?? audioEntry.BeginsAt) + skipTo,
        };
        return Playback.Play(trackInfo, audio, playAt, cancellationToken);
    }

    private async Task<IMessageProcess<PlayTrackCommand>> EnqueueNonStreamingEntry(
        Moment playAt,
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var audioBlobUri = ChatMediaResolver.GetAudioBlobUri(audioEntry);
        var audio = await AudioDownloader
            .Download(audioBlobUri, skipTo, cancellationToken)
            .ConfigureAwait(false);
        var trackInfo = new ChatAudioTrackInfo(audioEntry) {
            RecordedAt = audioEntry.BeginsAt + skipTo,
            ClientSideRecordedAt = (audioEntry.ClientSideBeginsAt ?? audioEntry.BeginsAt) + skipTo,
        };
        return Playback.Play(trackInfo, audio, playAt, cancellationToken);
    }
}
