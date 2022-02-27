using ActualChat.Audio;
using ActualChat.MediaPlayback;

namespace ActualChat.Chat.UI.Blazor.Services;

public abstract class ChatPlayer : IAsyncDisposable, IHasDisposeStarted
{
    protected static readonly TimeSpan InfDuration = 2 * Constants.Chat.MaxEntryDuration;
    private static long _playIndex;

    private ILogger? _log;
    private ChatEntryReader? _audioEntryReader;
    private ChatEntryReader? _textEntryReader;
    private ChatAuthor? _chatAuthor;

    protected ILogger Log => _log ??= Services.LogFor(GetType());
    protected ILogger? DebugLog => DebugMode ? Log : null;
    protected bool DebugMode => Constants.DebugMode.AudioPlayback;

    protected IServiceProvider Services { get; }
    protected MomentClockSet Clocks { get; }
    protected IChats Chats { get; }
    protected IChatAuthors ChatAuthors { get; }
    protected IChatMediaResolver MediaResolver { get; }
    protected AudioDownloader AudioDownloader { get; }
    protected IAudioStreamer AudioStreamer { get; }
    protected object Lock { get; } = new();

    public Session Session { get; init; } = Session.Null;
    public Symbol ChatId { get; init; } = default;
    public ChatEntryReader AudioEntryReader =>
        _audioEntryReader ??= Chats.CreateEntryReader(Session, ChatId, ChatEntryType.Audio);
    public ChatEntryReader TextEntryReader =>
        _textEntryReader ??= Chats.CreateEntryReader(Session, ChatId, ChatEntryType.Text);

    public IMutableState<Playback?> PlaybackState { get; }
    public Playback? Playback => PlaybackState.Value;
    public bool IsDisposeStarted { get; private set; }

    protected ChatPlayer(IServiceProvider services)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        Services = services;
        Clocks = Services.Clocks();
        Chats = Services.GetRequiredService<IChats>();
        ChatAuthors = Services.GetRequiredService<IChatAuthors>();
        MediaResolver = Services.GetRequiredService<IChatMediaResolver>();
        AudioDownloader = Services.GetRequiredService<AudioDownloader>();
        AudioStreamer = Services.GetRequiredService<IAudioStreamer>();
        PlaybackState = Services.StateFactory().NewMutable<Playback?>();
    }

    public virtual ValueTask DisposeAsync()
    {
        if (IsDisposeStarted)
            return ValueTask.CompletedTask;
        Playback? playback;
        lock (Lock) {
            if (IsDisposeStarted)
                return ValueTask.CompletedTask;
            IsDisposeStarted = true;
            playback = Playback;
        }
        return playback?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    public async Task Play(Moment startAt)
    {
        var playback = await Restart().ConfigureAwait(false);
        var cancellationToken = playback.StopToken;

        var playId = GetPlayId();
        var debugStopReason = "n/a";
        DebugLog?.LogDebug("Play #{PlayId}: started @ {StartAt}", playId, startAt);
        try {
            await PlayInternal(startAt, playback, cancellationToken).ConfigureAwait(false);
            await playback.Complete().ConfigureAwait(false);
            debugStopReason = "no more entries";
        }
        catch (OperationCanceledException) {
            debugStopReason = "cancellation";
            throw;
        }
        catch (Exception e) {
            debugStopReason = "error";
            Log.LogError(e, "Play #{PlayId}: failed", playId);
            try {
                await playback.Stop().ConfigureAwait(false);
            }
            catch {
                // Intended
            }
            throw;
        }
        finally {
            DebugLog?.LogDebug("Play #{PlayId}: ended ({StopReason})", playId, debugStopReason);
        }
    }

    public Task Complete()
    {
        var playback = Playback;
        return playback == null ? Task.CompletedTask : playback.Complete();
    }

    public Task Stop()
    {
        var playback = Playback;
        return playback == null ? Task.CompletedTask : playback.Stop();
    }

    // Protected & private methods

    protected abstract Task PlayInternal(Moment startAt, Playback playback, CancellationToken cancellationToken);

    protected string GetPlayId()
        => $"{Interlocked.Increment(ref _playIndex)} (chat #{ChatId})";

    protected async ValueTask<ChatAuthor?> GetChatAuthor(CancellationToken cancellationToken)
    {
        _chatAuthor ??= await ChatAuthors
            .GetChatAuthor(Session, ChatId, cancellationToken)
            .ConfigureAwait(false);
        return _chatAuthor;
    }

    protected async Task<Playback> Restart()
    {
        Playback? playback;
        while (true) {
            playback = Playback;
            if (playback is { IsStopped: false })
                await playback.Stop().ConfigureAwait(false);
            lock (Lock) {
                if (Playback is { IsStopped: false })
                    continue;
                playback = new Playback(Services, false);
                PlaybackState.Value = playback;
                break;
            }
        }

        playback.Start();
        return playback;
    }

    protected async ValueTask EnqueueEntry(
        Playback playback,
        Moment playAt,
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (audioEntry.Type != ChatEntryType.Audio)
                throw new NotSupportedException($"The entry's Type must be {ChatEntryType.Audio}.");
            if (audioEntry.Duration is {} duration && skipTo.TotalSeconds > duration) {
                DebugLog?.LogDebug(
                    "EnqueueEntry: chat #{ChatId}, entry #{EntryId}: skipTo={SkipTo:N}s > duration={Duration:N}s",
                    audioEntry.ChatId, audioEntry.Id, skipTo.TotalSeconds, duration);
                return;
            }
            if (audioEntry.IsStreaming)
                await EnqueueStreamingEntry(playback, playAt, audioEntry, skipTo, cancellationToken).ConfigureAwait(false);
            else
                await EnqueueNonStreamingEntry(playback, playAt, audioEntry, skipTo, cancellationToken).ConfigureAwait(false);
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

    private async ValueTask EnqueueStreamingEntry(
        Playback playback,
        Moment playAt,
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        try {
            DebugLog?.LogDebug(
                "EnqueueStreamingEntry: chat #{ChatId}, entry #{EntryId}, stream #{StreamId}, skipTo={SkipTo:N}",
                audioEntry.ChatId, audioEntry.Id, audioEntry.StreamId, skipTo.TotalSeconds);
            var audio = await AudioStreamer
                .GetAudio(audioEntry.StreamId, skipTo, cancellationToken)
                .ConfigureAwait(false);
            var audioWithoutWebM = audio.StripWebM(cancellationToken);
            var trackInfo = new ChatAudioTrackInfo(audioEntry) {
                RecordedAt = audioEntry.BeginsAt + skipTo,
                ClientSideRecordedAt = (audioEntry.ClientSideBeginsAt ?? audioEntry.BeginsAt) + skipTo,
            };
            await playback.AddMediaTrack(trackInfo, audioWithoutWebM, playAt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }

    private async ValueTask EnqueueNonStreamingEntry(
        Playback playback,
        Moment playAt,
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug(
            "EnqueueNonStreamingEntry: chat #{ChatId}, entry #{EntryId}, skipTo={SkipTo:N}s",
            audioEntry.ChatId, audioEntry.Id, skipTo.TotalSeconds);
        var audioBlobUri = MediaResolver.GetAudioBlobUri(audioEntry);
        var audio = await AudioDownloader
            .Download(audioBlobUri, skipTo, cancellationToken)
            .ConfigureAwait(false);
        var audioWithoutWebM = audio.StripWebM(cancellationToken);
        var trackInfo = new ChatAudioTrackInfo(audioEntry) {
            RecordedAt = audioEntry.BeginsAt + skipTo,
            ClientSideRecordedAt = (audioEntry.ClientSideBeginsAt ?? audioEntry.BeginsAt) + skipTo,
        };
        await playback.AddMediaTrack(trackInfo, audioWithoutWebM, playAt, cancellationToken).ConfigureAwait(false);
    }
}
