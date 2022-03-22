using System.Reactive.Concurrency;
using ActualChat.Audio;
using ActualChat.MediaPlayback;
using Microsoft.Extensions.Hosting;
using Stl.Locking;
using AsyncLock = Stl.Locking.AsyncLock;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatPlayer : IAsyncDisposable
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

    private readonly AsyncLock _stoppingLock = new(ReentryMode.CheckedFail);
    private CancellationTokenSource? _playCts;
    private readonly CancellationToken _applicationStopping;

    private int _isDisposed;
    private bool _isPlaying;

    /// <summary>
    /// Once enqueued, playback loop continues, so the larger is this duration,
    /// the higher is the chance to enqueue the next entry on time.
    /// </summary>
    private static readonly TimeSpan EnqueueAheadDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InfDuration = 2 * Constants.Chat.MaxEntryDuration;
    /// <summary> Min. delay is ~ 2.5*Ping, so we can skip something </summary>
    private static readonly TimeSpan StreamingSkipTo = TimeSpan.Zero;

    public readonly IMutableState<PlaybackKind> State;

    public Playback Playback { get; }

    public ChatPlayer(
        Symbol chatId,
        Session session,
        IHostApplicationLifetime lifetime,
        IPlaybackFactory playbackFactory,
        AudioDownloader audioDownloader,
        IChatMediaResolver mediaResolver,
        IAudioStreamer audioStreamer,
        IChatAuthors chatAuthors,
        IChats chats,
        IStateFactory stateFactory,
        MomentClockSet clocks,
        ILogger<ChatPlayer> log)
    {
        _log = log;
        _clocks = clocks;
        _chatId = chatId;
        _session = session;
        Playback = playbackFactory.Create();
        State = stateFactory.NewMutable<PlaybackKind>();
        _applicationStopping = lifetime.ApplicationStopping;
        _audioDownloader = audioDownloader;
        _mediaResolver = mediaResolver;
        _audioStreamer = audioStreamer;
        _chatAuthors = chatAuthors;
        _chats = chats;
    }

    public async Task Stop()
    {
        if (_isDisposed == 1)
            throw new ObjectDisposedException(nameof(ChatPlayer));
        if (!_isPlaying)
            return;
        using var _ = await _stoppingLock.Lock(CancellationToken.None).ConfigureAwait(false);
        await StopAndDisposeCancellation().ConfigureAwait(false);
    }

    private async Task StopAndDisposeCancellation()
    {
        if (!_isPlaying)
            return;
        var playCts = _playCts;
        _playCts = null;
        if (playCts != null) {
            // add the stop command to the playback command queue and when discard all related commands (except added stop)
            playCts.CancelAndDisposeSilently();
            var execution = await Playback.Stop(CancellationToken.None).ConfigureAwait(false);
            await execution.WhenCommandProcessed.ConfigureAwait(false);
        }
        _isPlaying = false;
    }

    /// <summary>
    /// Returns a <see cref="Task"/> which is completed when all <see cref="ChatEntry"/>
    /// from <paramref name="startAt"/> are enqueued to <see cref="Playback"/>
    /// </summary>
    public async Task Play(Moment startAt, bool isRealtime, CancellationToken cancellationToken)
    {
        if (_isDisposed == 1)
            throw new ObjectDisposedException(nameof(ChatPlayer));
        try {
            CancellationToken playCancellationToken;
            using (await _stoppingLock.Lock(CancellationToken.None).ConfigureAwait(false)) {
                if (_isPlaying)
                    await StopAndDisposeCancellation().ConfigureAwait(false);
                _playCts = CancellationTokenSource.CreateLinkedTokenSource(_applicationStopping, cancellationToken);
                playCancellationToken = _playCts.Token;
                _isPlaying = true;
            }

            State.Value = isRealtime
                ? PlaybackKind.Realtime
                : PlaybackKind.Historical;

            if (isRealtime)
                await PlayRealtime(startAt, playCancellationToken).ConfigureAwait(false);
            else
                await PlayHistorical(startAt, playCancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            _log.LogError(e, "ChatPlayer.Play failed");
        }
        finally {
            State.Value = PlaybackKind.None;
        }
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
        var playbackEndedQueue = new ConcurrentDictionary<Task, Task>();
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
            var (_, _, whenPlaybackEnded) =  await EnqueueEntry(Playback, cpuClock.Now, entry, skipTo, cancellationToken)
                .ConfigureAwait(false);

            if (whenPlaybackEnded.IsCompleted) continue;

            playbackEndedQueue.TryAdd(whenPlaybackEnded, whenPlaybackEnded);
            _ = whenPlaybackEnded.ContinueWith(t => playbackEndedQueue.TryRemove(t, out _), cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        await Task.WhenAll(playbackEndedQueue.Values).ConfigureAwait(false);
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
        var playbackEndedQueue = new ConcurrentDictionary<Task, Task>();
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
            var (_, _, whenPlaybackEnded) =  await EnqueueEntry(Playback, playAt, entry, skipTo, cancellationToken)
                .ConfigureAwait(false);

            if (whenPlaybackEnded.IsCompleted) continue;

            playbackEndedQueue.TryAdd(whenPlaybackEnded, whenPlaybackEnded);
            _ = whenPlaybackEnded.ContinueWith(t => playbackEndedQueue.TryRemove(t, out _), cancellationToken, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        await Task.WhenAll(playbackEndedQueue.Values).ConfigureAwait(false);
    }

    private async ValueTask<Playback.CommandExecution> EnqueueEntry(
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

            if (audioEntry.Duration is { } duration && skipTo.TotalSeconds > duration)
                return Playback.CommandExecution.None;
            if (audioEntry.IsStreaming)
                return await EnqueueStreamingEntry(playback, playAt, audioEntry, skipTo, cancellationToken)
                    .ConfigureAwait(false);

            return await EnqueueNonStreamingEntry(playback, playAt, audioEntry, skipTo, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            _log.LogError(e,
                "Error playing audio entry; chat #{ChatId}, entry #{AudioEntryId}, stream #{StreamId}",
                audioEntry.ChatId,
                audioEntry.Id,
                audioEntry.StreamId);
            throw;
        }
    }

    private async ValueTask<Playback.CommandExecution> EnqueueStreamingEntry(
        Playback playback,
        Moment playAt,
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var audio = await _audioStreamer
            .GetAudio(audioEntry.StreamId, skipTo, cancellationToken)
            .ConfigureAwait(false);
        var trackInfo = new ChatAudioTrackInfo(audioEntry) {
            RecordedAt = audioEntry.BeginsAt + skipTo,
            ClientSideRecordedAt = (audioEntry.ClientSideBeginsAt ?? audioEntry.BeginsAt) + skipTo,
        };
        return await playback.Play(trackInfo, audio, playAt, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Playback.CommandExecution> EnqueueNonStreamingEntry(
        Playback playback,
        Moment playAt,
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var audioBlobUri = _mediaResolver.GetAudioBlobUri(audioEntry);
        var audio = await _audioDownloader
            .Download(audioBlobUri, skipTo, cancellationToken)
            .ConfigureAwait(false);
        var trackInfo = new ChatAudioTrackInfo(audioEntry) {
            RecordedAt = audioEntry.BeginsAt + skipTo,
            ClientSideRecordedAt = (audioEntry.ClientSideBeginsAt ?? audioEntry.BeginsAt) + skipTo,
        };
        return await playback.Play(trackInfo, audio, playAt, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask DisposeAsyncCore()
    {
        try {
            await StopAndDisposeCancellation().ConfigureAwait(false);
        }
        finally {
            await Playback.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) == 1)
            return;
        await DisposeAsyncCore().ConfigureAwait(false);
    }
}
