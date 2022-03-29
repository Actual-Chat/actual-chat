using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.Messaging;
using Microsoft.Extensions.Hosting;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class ChatPlayer : ProcessorBase
{
    /// <summary>
    /// Once enqueued, playback loop continues, so the larger is this duration,
    /// the higher is the chance to enqueue the next entry on time.
    /// </summary>
    private static readonly TimeSpan EnqueueAheadDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InfDuration = 2 * Constants.Chat.MaxEntryDuration;
    /// <summary> Min. delay is ~ 2.5*Ping, so we can skip something </summary>
    private static readonly TimeSpan StreamingSkipTo = TimeSpan.Zero;

    private readonly Symbol _chatId;
    private readonly AudioDownloader _audioDownloader;
    private readonly IChatMediaResolver _mediaResolver;
    private readonly IAudioStreamer _audioStreamer;
    private readonly IChatAuthors _chatAuthors;
    private readonly Session _session;
    private readonly IChats _chats;

    private readonly MomentClockSet _clocks;
    private readonly ILogger<ChatPlayer> _log;

    private readonly AsyncLock _stoppingLock = new(ReentryMode.CheckedFail);
    private CancellationTokenSource? _playStopTokenSource;
    private bool _isPlaying;

    public readonly IMutableState<PlaybackKind> State;
    public Playback Playback { get; }

    public ChatPlayer(
        Symbol chatId,
        Session session,
        IPlaybackFactory playbackFactory,
        AudioDownloader audioDownloader,
        IChatMediaResolver mediaResolver,
        IAudioStreamer audioStreamer,
        IChatAuthors chatAuthors,
        IChats chats,
        IStateFactory stateFactory,
        IHostApplicationLifetime lifetime,
        MomentClockSet clocks,
        ILogger<ChatPlayer> log)
        : base(CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping))
    {
        _log = log;
        _clocks = clocks;

        _chatId = chatId;
        _session = session;
        Playback = playbackFactory.Create();
        State = stateFactory.NewMutable<PlaybackKind>();
        _audioDownloader = audioDownloader;
        _mediaResolver = mediaResolver;
        _audioStreamer = audioStreamer;
        _chatAuthors = chatAuthors;
        _chats = chats;
    }

    protected override async Task DisposeAsyncCore()
    {
        try {
            await StopInternal().ConfigureAwait(false);
        }
        catch {
            // Intended
        }
        await Playback.DisposeAsync().ConfigureAwait(false);
    }

    public async Task Stop()
    {
        this.ThrowIfDisposedOrDisposing();
        if (!_isPlaying)
            return;
        using var _ = await _stoppingLock.Lock(CancellationToken.None).ConfigureAwait(false);
        await StopInternal().ConfigureAwait(false);
    }

    /// <summary>
    /// Returns a <see cref="Task"/> which is completed when all <see cref="ChatEntry"/>
    /// from <paramref name="startAt"/> are enqueued to <see cref="Playback"/>
    /// </summary>
    public async Task Play(Moment startAt, bool isRealtime, CancellationToken cancellationToken)
    {
        this.ThrowIfDisposedOrDisposing();
        try {
            CancellationToken playStopToken;
            using (await _stoppingLock.Lock(CancellationToken.None).ConfigureAwait(false)) {
                if (_isPlaying)
                    await StopInternal().ConfigureAwait(false);
                _playStopTokenSource = cancellationToken.LinkWith(StopToken);
                playStopToken = _playStopTokenSource.Token;
                _isPlaying = true;
            }

            State.Value = isRealtime
                ? PlaybackKind.Realtime
                : PlaybackKind.Historical;

            if (isRealtime)
                await PlayRealtime(startAt, playStopToken).ConfigureAwait(false);
            else
                await PlayHistorical(startAt, playStopToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            _log.LogError(e, "ChatPlayer.Play failed. ChatId: {ChatId}", _chatId);
        }
        finally {
            State.Value = PlaybackKind.None;
        }
    }

    // Private methods

    private async Task StopInternal()
    {
        if (!_isPlaying)
            return;
        var playStopTokenSource = _playStopTokenSource;
        _playStopTokenSource = null;
        if (playStopTokenSource != null) {
            // add the stop command to the playback command queue and when discard all related commands (except added stop)
            playStopTokenSource.CancelAndDisposeSilently();
            var stopProcess = Playback.Stop(CancellationToken.None);
            await stopProcess.WhenStarted.ConfigureAwait(false);
        }
        _isPlaying = false;
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
        var playProcesses = new ConcurrentDictionary<IMessageProcess<PlayTrackCommand>, Unit>();
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

            var playProcess = await EnqueueEntry(Playback, cpuClock.Now, entry, skipTo, cancellationToken).ConfigureAwait(false);
            if (playProcess.WhenCompleted.IsCompleted)
                continue;

            playProcesses.TryAdd(playProcess, default);
#pragma warning disable VSTHRD105
            _ = playProcess.WhenCompleted.ContinueWith(
                t => playProcesses.TryRemove(playProcess, out _),
                TaskContinuationOptions.ExecuteSynchronously);
#pragma warning restore VSTHRD105
        }

        await Task.WhenAll(playProcesses.Keys.Select(s => s.WhenCompleted)).ConfigureAwait(false);
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
        var playProcesses = new ConcurrentDictionary<IMessageProcess<PlayTrackCommand>, Unit>();
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

            var playProcess = await EnqueueEntry(Playback, playAt, entry, skipTo, cancellationToken).ConfigureAwait(false);
            if (playProcess.WhenCompleted.IsCompleted)
                continue;

            playProcesses.TryAdd(playProcess, default);
#pragma warning disable VSTHRD105
            _ = playProcess.WhenCompleted.ContinueWith(
                t => playProcesses.TryRemove(playProcess, out _),
                TaskContinuationOptions.ExecuteSynchronously);
#pragma warning restore VSTHRD105
        }

        await Task.WhenAll(playProcesses.Keys.Select(s => s.WhenCompleted)).ConfigureAwait(false);
    }

    private async Task<IMessageProcess<PlayTrackCommand>> EnqueueEntry(
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
                return PlayTrackCommand.PlayNothingProcess;
            return await (audioEntry.IsStreaming
                ? EnqueueStreamingEntry(playback, playAt, audioEntry, skipTo, cancellationToken)
                : EnqueueNonStreamingEntry(playback, playAt, audioEntry, skipTo, cancellationToken)
                ).ConfigureAwait(false);
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

    private async Task<IMessageProcess<PlayTrackCommand>> EnqueueStreamingEntry(
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
        return playback.Play(trackInfo, audio, playAt, cancellationToken);
    }

    private async Task<IMessageProcess<PlayTrackCommand>> EnqueueNonStreamingEntry(
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
        return playback.Play(trackInfo, audio, playAt, cancellationToken);
    }
}
