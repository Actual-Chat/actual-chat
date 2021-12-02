using ActualChat.Audio;
using ActualChat.Playback;
using Cysharp.Text;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatMediaPlayer : IAsyncDisposable
{
    private static long _lastPlayIndex;

    private IChats Chats { get; }
    private IChatAuthors ChatAuthors { get; }
    private IChatMediaResolver MediaResolver { get; }
    private AudioDownloader AudioDownloader { get; }
    private IAudioSourceStreamer AudioSourceStreamer { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode { get; } = Constants.DebugMode.AudioPlayback;

    public Session Session { get; init; } = Session.Null;
    public ChatId ChatId { get; init; } = default;
    public bool IsRealTimePlayer { get; init; }

    // This should be approximately 2.5 x ping time
    public TimeSpan RealtimeNowOffset { get; init; } = TimeSpan.FromMilliseconds(250);
    // Once enqueued, playback loop continues, so the larger is this gap, the higher is the chance
    // to enqueue the next entry on time.
    public TimeSpan EnqueueToPlaybackGap { get; init; } = TimeSpan.FromSeconds(3);

    public MediaPlayer MediaPlayer { get; }
    public bool IsPlaying => MediaPlayer.PlaybackState.IsPlaying;
    public bool IsDisposed => MediaPlayer.IsDisposed;

    public ChatMediaPlayer(IServiceProvider services)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        Log = services.LogFor(GetType());
        Chats = services.GetRequiredService<IChats>();
        ChatAuthors = services.GetRequiredService<IChatAuthors>();
        MediaPlayer = services.GetRequiredService<MediaPlayer>();
        MediaResolver = services.GetRequiredService<IChatMediaResolver>();
        AudioDownloader = services.GetRequiredService<AudioDownloader>();
        AudioSourceStreamer = services.GetRequiredService<IAudioSourceStreamer>();
        Clocks = services.Clocks();
    }

    public ValueTask DisposeAsync()
        => MediaPlayer.DisposeAsync();

    public Task Play()
        => Play(Clocks.CpuClock.Now);

    public async Task Play(Moment startAt)
    {
        await MediaPlayer.Stop().ConfigureAwait(false);
        var playbackState = MediaPlayer.Play();
        var cancellationToken = MediaPlayer.StopToken;
        var clock = Clocks.CpuClock;
        var infDuration = 2 * Constants.Chat.MaxEntryDuration;
        var nowOffset = IsRealTimePlayer ? RealtimeNowOffset : TimeSpan.Zero;
        var chatAuthor = (ChatAuthor?) null;

        var playIndex = Interlocked.Increment(ref _lastPlayIndex);
        var playId = $"{playIndex} (chat #{ChatId}, {(IsRealTimePlayer ? "real-time" : "historical")})";
        var debugStopReason = "n/a";
        DebugLog?.LogDebug("Play #{PlayId}: started @ {StartAt}", playId, startAt);
        try {
            var entryReader = Chats.CreateEntryReader(Session, ChatId);
            var idRange = await Chats.GetIdRange(Session, ChatId, cancellationToken).ConfigureAwait(false);
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
            await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false)) {
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
                await EnqueueEntry(playAt - nowOffset, entry, entrySkipTo, cancellationToken).ConfigureAwait(false);
                realtimeBlockEnd = Moment.Max(realtimeBlockEnd, entryEndsAt + realtimeOffset);
            }
            MediaPlayer.Complete();
            await playbackState.PlayingTask.ConfigureAwait(false);
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
                await MediaPlayer.Stop().ConfigureAwait(false);
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

    public Task Stop()
        => MediaPlayer.Stop();

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

    private async Task<Symbol> EnqueueEntry(
        Moment playAt,
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        try {
            if (audioEntry.Type != ChatEntryType.Audio)
                throw new NotSupportedException($"The entry's Type must be {ChatEntryType.Audio}.");
            return audioEntry.IsStreaming
                ? await EnqueueStreamingEntry(playAt, audioEntry, skipTo, cancellationToken).ConfigureAwait(false)
                : await EnqueueNonStreamingEntry(playAt, audioEntry, skipTo, cancellationToken).ConfigureAwait(false);
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

    private async Task<Symbol> EnqueueStreamingEntry(
        Moment playAt,
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug(
            "EnqueueStreamingEntry: chat #{ChatId}, entry #{EntryId}, stream #{StreamId}",
            audioEntry.ChatId,
            audioEntry.Id,
            audioEntry.StreamId);
        var trackId = GetAudioTrackId(audioEntry);
        var audio = await AudioSourceStreamer
            .GetAudio(audioEntry.StreamId, skipTo, cancellationToken)
            .ConfigureAwait(false);
        await MediaPlayer.AddMediaTrack(trackId,
                playAt,
                audioEntry.BeginsAt,
                audio,
                skipTo,
                cancellationToken)
            .ConfigureAwait(false);
        return trackId;
    }

    private async Task<Symbol> EnqueueNonStreamingEntry(
        Moment playAt,
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogDebug(
            "EnqueueNonStreamingEntry: chat #{ChatId}, entry #{EntryId}",
            audioEntry.ChatId,
            audioEntry.Id);
        var audioBlobUri = MediaResolver.GetAudioBlobUri(audioEntry);
        var audio = await AudioDownloader
            .Download(audioBlobUri, skipTo, cancellationToken)
            .ConfigureAwait(false);
        var trackId = GetAudioTrackId(audioEntry);
        await MediaPlayer.AddMediaTrack(
                trackId,
                playAt,
                audioEntry.BeginsAt,
                audio,
                skipTo,
                cancellationToken)
            .ConfigureAwait(false);
        return trackId;
    }
}
