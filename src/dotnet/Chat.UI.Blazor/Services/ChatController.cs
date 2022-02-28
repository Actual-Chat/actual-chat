using ActualChat.Audio;
using ActualChat.MediaPlayback;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatPlayer : IAsyncDisposable
{
    private readonly Symbol _chatId;
    private readonly AudioDownloader _audioDownloader;
    private readonly ILogger<ChatPlayer> _log;
    private readonly IChatMediaResolver _mediaResolver;
    private readonly IAudioStreamer _audioStreamer;
    private readonly IChatAuthors _chatAuthors;
    private readonly MomentClockSet _clocks;
    private readonly Session _session;
    private readonly IChats _chats;

    /// <summary>
    /// Once enqueued, playback loop continues, so the larger is this duration,
    /// the higher is the chance to enqueue the next entry on time.
    /// </summary>
    private static readonly TimeSpan EnqueueAheadDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InfDuration = 2 * Constants.Chat.MaxEntryDuration;
    /// <summary> Min. delay is ~ 2.5*Ping, so we can skip something </summary>
    private static readonly TimeSpan StreamingSkipTo = TimeSpan.Zero;

    public IMutableState<Playback> PlaybackState { get; }

    public ChatPlayer(
        Symbol chatId,
        IStateFactory stateFactory,
        Playback playback,
        AudioDownloader audioDownloader,
        ILogger<ChatPlayer> log,
        IChatMediaResolver mediaResolver,
        IAudioStreamer audioStreamer,
        IChatAuthors chatAuthors,
        MomentClockSet clocks,
        Session session,
        IChats chats
        )
    {
        PlaybackState = stateFactory.NewMutable<Playback>();
        PlaybackState.Value = playback;
        _chatId = chatId;
        _audioDownloader = audioDownloader;
        _log = log;
        _mediaResolver = mediaResolver;
        _audioStreamer = audioStreamer;
        _chatAuthors = chatAuthors;
        _clocks = clocks;
        _session = session;
        _chats = chats;
    }

    // TODO: maybe we can refactor this & merge historical and realtime player or move them out like IChatEntryProvider
    public Task Play(Moment startAt, bool isRealtime, CancellationToken cancellationToken)
    {
        var runningTask = PlaybackState.Value.Run(cancellationToken);
        _ = isRealtime
            ? PlayRealtime(startAt, cancellationToken)
            : PlayHistorical(startAt, cancellationToken);
        return runningTask;
    }

    private async Task PlayRealtime(Moment startAt, CancellationToken cancellationToken)
    {
        var cpuClock = _clocks.CpuClock;
        var audioEntryReader = _chats.CreateEntryReader(_session, _chatId, ChatEntryType.Audio);
        var idRange = await _chats.GetIdRange(_session, _chatId, ChatEntryType.Audio, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await audioEntryReader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        var startId = startEntry?.Id ?? idRange.End - 1;

        var entries = audioEntryReader.ReadAllWaitingForNew(startId, cancellationToken);
        await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            if (entry.EndsAt < startAt)
                // We're starting @ (startAt - ChatConstants.MaxEntryDuration),
                // so we need to skip a few entries.
                // Note that streaming entries have EndsAt == null, so we don't skip them.
                continue;

            if (!Constants.DebugMode.AudioPlaybackPlayMyOwnAudio) {
                var chatAuthor = await _chatAuthors.GetChatAuthor(_session, _chatId, cancellationToken)
                    .ConfigureAwait(false);
                if (chatAuthor != null && entry.AuthorId == chatAuthor.Id) {
                    continue;
                }
            }

            var skipToOffset = entry.IsStreaming ? StreamingSkipTo : TimeSpan.Zero;
            var entryBeginsAt = Moment.Max(entry.BeginsAt + skipToOffset, startAt);
            var skipTo = entryBeginsAt - entry.BeginsAt;
            await EnqueueEntry(PlaybackState.Value, cpuClock.Now, entry, skipTo, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task PlayHistorical(Moment startAt, CancellationToken cancellationToken)
    {
        var cpuClock = _clocks.CpuClock;
        var audioEntryReader = _chats.CreateEntryReader(_session, _chatId, ChatEntryType.Audio);
        var idRange = await _chats.GetIdRange(_session, _chatId, ChatEntryType.Audio, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await audioEntryReader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        if (startEntry == null) {
            _log.LogWarning("Couldn't find start entry");
            return;
        }

        var playbackBlockEnd = cpuClock.Now - TimeSpan.FromDays(1); // Any time in past
        var playbackOffset = playbackBlockEnd - Moment.EpochStart; // now - playTime

        idRange = (startEntry.Id, idRange.End);
        var entries = audioEntryReader.ReadAll(idRange, cancellationToken);
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (!entry.StreamId.IsEmpty) // Streaming entry
                continue;
            if (entry.EndsAt < startAt)
                // We're normally starting @ (startAt - ChatConstants.MaxEntryDuration),
                // so we need to skip a few entries.
                continue;

            var now = cpuClock.Now;
            var entryBeginsAt = Moment.Max(entry.BeginsAt, startAt);
            var entryEndsAt = entry.EndsAt ?? entry.BeginsAt + InfDuration;
            entryEndsAt = Moment.Min(entryEndsAt, entry.ContentEndsAt ?? entryEndsAt);
            var skipTo = entryBeginsAt - entry.BeginsAt;
            if (playbackBlockEnd < entryBeginsAt + playbackOffset) {
                // There is a gap between the currently playing "block" and the entry.
                // This means we're still playing the "historical" block, and the new entry
                // starts with some gap after it; we're going to nullify this gap here by
                // adjusting realtimeOffset.
                playbackBlockEnd = Moment.Max(now, playbackBlockEnd);
                playbackOffset = playbackBlockEnd - entryBeginsAt;
            }

            var playAt = entryBeginsAt + playbackOffset;
            playbackBlockEnd = Moment.Max(playbackBlockEnd, entryEndsAt + playbackOffset);

            var enqueueDelay = playAt - now - EnqueueAheadDuration;
            if (enqueueDelay > TimeSpan.Zero)
                await cpuClock.Delay(enqueueDelay, cancellationToken).ConfigureAwait(false);
            await EnqueueEntry(PlaybackState.Value, playAt, entry, skipTo, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public void Stop() => PlaybackState.Value.StopPlaying();

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
            if (audioEntry.Duration is { } duration && skipTo.TotalSeconds > duration) {
                return;
            }
            if (audioEntry.IsStreaming) {
                await EnqueueStreamingEntry(playback, playAt, audioEntry, skipTo, cancellationToken)
                    .ConfigureAwait(false);
            }
            else {
                await EnqueueNonStreamingEntry(playback, playAt, audioEntry, skipTo, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            _log.LogError(e,
                "Error playing audio entry; chat #{_chatId}, entry #{AudioEntryId}, stream #{StreamId}",
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
            var audio = await _audioStreamer
                .GetAudio(audioEntry.StreamId, skipTo, cancellationToken)
                .ConfigureAwait(false);
            var audioWithoutWebM = audio.StripWebM(cancellationToken);
            var trackInfo = new ChatAudioTrackInfo(audioEntry) {
                RecordedAt = audioEntry.BeginsAt + skipTo,
                ClientSideRecordedAt = (audioEntry.ClientSideBeginsAt ?? audioEntry.BeginsAt) + skipTo,
            };
            await playback.Play(trackInfo, audioWithoutWebM, playAt, cancellationToken).ConfigureAwait(false);
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
        var audioBlobUri = _mediaResolver.GetAudioBlobUri(audioEntry);
        var audio = await _audioDownloader
            .Download(audioBlobUri, skipTo, stripWebm: true, cancellationToken)
            .ConfigureAwait(false);
        var audioWithoutWebM = audio.StripWebM(cancellationToken);
        var trackInfo = new ChatAudioTrackInfo(audioEntry) {
            RecordedAt = audioEntry.BeginsAt + skipTo,
            ClientSideRecordedAt = (audioEntry.ClientSideBeginsAt ?? audioEntry.BeginsAt) + skipTo,
        };
        await playback.Play(trackInfo, audioWithoutWebM, playAt, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        // TODO: implement
        return default;
    }
}

internal interface IChatPlayerFactory
{
    ChatPlayer Create(Symbol chatId, Symbol userId);
}

/// <summary> Must be scoped service. </summary>
public sealed class ChatController : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Symbol, ChatPlayer> _players = new();
    private readonly IChatPlayerFactory _factory;
    private readonly ILogger<ChatController> _log;
    private int _isDisposed;

    internal ChatController(ILogger<ChatController> log, IChatPlayerFactory factory)
    {
        _log = log;
        _factory = factory;
    }

    public ChatPlayer GetPlayer(ChatAuthor author) => GetPlayer(author.ChatId, author.UserId);

    public ChatPlayer GetPlayer(Symbol chatId, Symbol userId)
    {
        if (_isDisposed == 1)
            throw new ObjectDisposedException(nameof(ChatController));
        return _players.GetOrAdd(chatId, static (key, arg) => arg.self._factory.Create(key, arg.userId), new { self = this, userId });
    }

    /// <summary> Disposes all resources allocated for <paramref name="chatId"/> </summary>
    public async ValueTask Close(Symbol chatId)
    {
        if (_isDisposed == 1)
            throw new ObjectDisposedException(nameof(ChatController));
        if (_players.TryRemove(chatId, out var player)) {
            await player.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0)
            return;

        GC.SuppressFinalize(this);

        var playerDisposeTasks = _players
            .Select(kv => DisposePlayer(kv.Key, kv.Value))
            .ToArray();

        _players.Clear();
        if (playerDisposeTasks.Length > 0)
            await Task.WhenAll(playerDisposeTasks).ConfigureAwait(false);

        async Task DisposePlayer(Symbol chatId, ChatPlayer player)
        {
            try {
                await player.DisposeAsync().ConfigureAwait(true);
            }
            catch (Exception e) {
                _log.LogError(e, "Can't dispose player for chatId:{chatId}", chatId);
            }
        }
    }
}