using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using ActualChat.Audio;
using ActualChat.MediaPlayback;
using Cysharp.Text;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatPlayer : IAsyncDisposable, IHasDisposeStarted
{
    private static long _lastPlayIndex;
    private ILogger? _log;

    private ILogger Log => _log ??= Services.LogFor(GetType());
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.AudioPlayback;

    private IServiceProvider Services { get; }
    private MomentClockSet Clocks { get; }
    private IChats Chats { get; }
    private IChatAuthors ChatAuthors { get; }
    private IChatMediaResolver MediaResolver { get; }
    private AudioDownloader AudioDownloader { get; }
    private IAudioSourceStreamer AudioSourceStreamer { get; }
    private object Lock { get; } = new();

    public Session Session { get; init; } = Session.Null;
    public Symbol ChatId { get; init; } = default;
    public bool IsRealTimePlayer { get; init; }

    // This should be approximately 2.5 x ping time
    public TimeSpan RealtimeNowOffset { get; init; } = TimeSpan.FromMilliseconds(-50);
    // Once enqueued, playback loop continues, so the larger is this gap, the higher is the chance
    // to enqueue the next entry on time.
    public TimeSpan EnqueueToPlaybackGap { get; init; } = TimeSpan.FromSeconds(3);

    public IMutableState<Playback?> PlaybackState { get; }
    public Playback? Playback => PlaybackState.Value;
    public bool IsDisposeStarted { get; private set; }

    public ChatPlayer(IServiceProvider services)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        Services = services;
        Clocks = Services.Clocks();
        Chats = Services.GetRequiredService<IChats>();
        ChatAuthors = Services.GetRequiredService<IChatAuthors>();
        MediaResolver = Services.GetRequiredService<IChatMediaResolver>();
        AudioDownloader = Services.GetRequiredService<AudioDownloader>();
        AudioSourceStreamer = Services.GetRequiredService<IAudioSourceStreamer>();
        PlaybackState = Services.StateFactory().NewMutable<Playback?>();
    }

    public ValueTask DisposeAsync()
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

    public async Task Play(Moment startAt)
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

        var cancellationToken = playback.StopToken;
        var clock = Clocks.CpuClock;
        var infDuration = 2 * Constants.Chat.MaxEntryDuration;
        var nowOffset = IsRealTimePlayer ? RealtimeNowOffset : TimeSpan.Zero;
        var chatAuthor = (ChatAuthor?) null;

        var playIndex = Interlocked.Increment(ref _lastPlayIndex);
        var playId = $"{playIndex} (chat #{ChatId}, {(IsRealTimePlayer ? "real-time" : "historical")})";
        var debugStopReason = "n/a";
        DebugLog?.LogDebug("Play #{PlayId}: started @ {StartAt}", playId, startAt);
        try {
            var entryReader = Chats.CreateEntryReader(Session, ChatId, ChatEntryType.Audio);
            var idRange = await Chats.GetIdRange(Session, ChatId, ChatEntryType.Audio, cancellationToken).ConfigureAwait(false);
            var startEntry = await entryReader
                .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
                .ConfigureAwait(false);
            idRange = (startEntry?.Id ?? idRange.Start, idRange.End);
            var now = clock.Now + nowOffset;
            var realtimeOffset = IsRealTimePlayer ? TimeSpan.Zero : now - startAt;
            var realtimeBlockEnd = now;

            var entries = (IsRealTimePlayer
                ? entryReader.ReadAllWaitingForNew(idRange.Start, cancellationToken)
                : entryReader.ReadAll(idRange, cancellationToken))
                .Where(e => e.Type == ChatEntryType.Audio);
            await foreach (var entry in entries.ConfigureAwait(false)) {
                if (entry.EndsAt < startAt) // We're normally starting @ (startAt - ChatConstants.MaxEntryDuration),
                    // so we need to skip a few entries.
                    // Note that streaming entries have EndsAt == null, so we don't skip them.
                    continue;

                now = clock.Now + nowOffset;
                if (IsRealTimePlayer) {
                    startAt = now;
                    realtimeBlockEnd = now;
                    realtimeOffset = TimeSpan.Zero;

                    if (!Constants.DebugMode.AudioPlaybackPlayMyOwnAudio) {
                        // It can't change once it's created, so we want to fetch it just once
                        chatAuthor ??= await ChatAuthors
                            .GetSessionChatAuthor(Session, ChatId, cancellationToken)
                            .ConfigureAwait(false);
                        if (chatAuthor != null && entry.AuthorId == chatAuthor.Id)
                            continue;
                    }
                }

                var entryBeginsAt = Moment.Max(entry.BeginsAt, startAt);
                var entryEndsAt = entry.EndsAt ?? entry.BeginsAt + infDuration;
                var entrySkipTo = entryBeginsAt - entry.BeginsAt;

                if (!IsRealTimePlayer && entryBeginsAt + realtimeOffset > realtimeBlockEnd) {
                    // There is a gap between the currently playing "block" and the entry.
                    // This means we're still playing the "historical" block, and the new entry
                    // starts with some gap after it; we're going to nullify this gap here by
                    // adjusting realtimeOffset.
                    realtimeBlockEnd = Moment.Max(now, realtimeBlockEnd);
                    realtimeOffset = realtimeBlockEnd - entryBeginsAt;
                }

                var playAt = entryBeginsAt + realtimeOffset;
                var enqueueDelay = playAt - now - EnqueueToPlaybackGap;
                if (enqueueDelay > TimeSpan.Zero)
                    await clock.Delay(enqueueDelay, cancellationToken).ConfigureAwait(false);
                await EnqueueEntry(playback, playAt - nowOffset, entry, entrySkipTo, cancellationToken)
                    .ConfigureAwait(false);
                realtimeBlockEnd = Moment.Max(realtimeBlockEnd, entryEndsAt + realtimeOffset);
            }
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

    public static Symbol GetAudioTrackId(ChatEntry chatEntry)
    {
        var entryId = chatEntry.Type switch {
            ChatEntryType.Text => chatEntry.AudioEntryId ?? -1,
            ChatEntryType.Audio => chatEntry.Id,
            _ => -1,
        };
        return entryId >= 0
            ? ZString.Concat("audio:", chatEntry.ChatId, ":", entryId)
            : throw new InvalidOperationException("Provided chat entry has no associated audio track.");
    }

    // Private  methods

    private async ValueTask EnqueueEntry(
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
                "EnqueueStreamingEntry: chat #{ChatId}, entry #{EntryId}, stream #{StreamId}",
                audioEntry.ChatId,
                audioEntry.Id,
                audioEntry.StreamId);
            var trackId = GetAudioTrackId(audioEntry);
            var audio = await AudioSourceStreamer
                .GetAudio(audioEntry.StreamId, skipTo, cancellationToken)
                .ConfigureAwait(false);
            await playback.AddMediaTrack(trackId,
                    playAt,
                    audioEntry.BeginsAt,
                    audio,
                    skipTo,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
    }

    [RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Deserialize<ActualChat.Audio.AudioMetadata>(string, System.Text.Json.JsonSerializerOptions?)")]
    private async ValueTask EnqueueNonStreamingEntry(
        Playback playback,
        Moment playAt,
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug(
            "EnqueueNonStreamingEntry: chat #{ChatId}, entry #{EntryId}",
            audioEntry.ChatId,
            audioEntry.Id);
        var metaData = audioEntry.Metadata.IsNullOrEmpty()
            ? new AudioMetadata()
            : JsonSerializer.Deserialize<AudioMetadata>(audioEntry.Metadata);
        var audioBlobUri = MediaResolver.GetAudioBlobUri(audioEntry);
        var audio = await AudioDownloader
            .Download(audioBlobUri, metaData, skipTo, cancellationToken)
            .ConfigureAwait(false);
        var trackId = GetAudioTrackId(audioEntry);
        await playback.AddMediaTrack(
                trackId,
                playAt,
                audioEntry.BeginsAt,
                audio,
                skipTo,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
