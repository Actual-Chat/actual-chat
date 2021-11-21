using ActualChat.Audio;
using ActualChat.Playback;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatMediaPlayer : IDisposable
{
    private IChats Chats { get; }
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
    public Option<AuthorId> SilencedAuthorId { get; init; }
    public TimeSpan EnqueueToPlaybackDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public MediaPlayer MediaPlayer { get; }
    public bool IsPlaying => MediaPlayer.IsPlaying;

    public ChatMediaPlayer(IServiceProvider services)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        Log = services.LogFor(GetType());
        Chats = services.GetRequiredService<IChats>();
        MediaPlayer = services.GetRequiredService<MediaPlayer>();
        MediaResolver = services.GetRequiredService<IChatMediaResolver>();
        AudioDownloader = services.GetRequiredService<AudioDownloader>();
        AudioSourceStreamer = services.GetRequiredService<IAudioSourceStreamer>();
        Clocks = services.Clocks();
    }

    public void Dispose()
        => MediaPlayer.Dispose();

    public Task Play()
        => Play(Clocks.SystemClock.Now);

    public async Task Play(Moment startAt)
    {
        await MediaPlayer.Stop().ConfigureAwait(false);
        var playTask = MediaPlayer.Play();
        var cancellationToken = MediaPlayer.StopToken;
        var clock = Clocks.CpuClock;
        var infDuration = 2 * ChatConstants.MaxEntryDuration;

        DebugLog?.LogInformation(
            "Play({StartAt}) started for chat #{ChatId} / {PlaybackKind}",
            startAt, ChatId, IsRealTimePlayer ? "real-time" : "historical");
        try {
            var entryReader = Chats.CreateEntryReader(Session, ChatId);
            var startEntryId = await entryReader
                .GetNextEntryId(startAt - ChatConstants.MaxEntryDuration, cancellationToken)
                .ConfigureAwait(false);
            var now = clock.Now;
            var realtimeOffset = now - startAt;
            var realtimeBlockEnd = now;

            var entries = entryReader
                .GetAllAfter(startEntryId, IsRealTimePlayer, cancellationToken)
                .Where(e => e.Type == ChatEntryType.Audio);
            await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (entry.EndsAt < startAt) // We're normally starting @ (startAt - ChatConstants.MaxEntryDuration),
                    // so we need to skip a few entries.
                    // Note that streaming entries have EndsAt == null, so we don't skip them.
                    continue;
                if (SilencedAuthorId.IsSome(out var silencedAuthorId) && entry.AuthorId == silencedAuthorId)
                    continue;

                now = clock.Now;
                var entryBeginsAt = Moment.Max(entry.BeginsAt, startAt);
                var entryEndsAt = entry.EndsAt ?? entry.BeginsAt + infDuration;
                var entrySkipTo = entryBeginsAt - entry.BeginsAt;

                if (entryBeginsAt + realtimeOffset > realtimeBlockEnd) {
                    // There is a gap between the currently playing "block" and the entry.
                    // This means we're still playing the "historical" block, and the new entry
                    // starts with some gap after it; we're going to nullify this gap here by
                    // adjusting realtimeOffset.
                    realtimeBlockEnd = Moment.Max(now, realtimeBlockEnd);
                    realtimeOffset = realtimeBlockEnd - entryBeginsAt;
                }

                var realtimeBeginsAt = entryBeginsAt + realtimeOffset;
                var enqueueDelay = realtimeBeginsAt - now - EnqueueToPlaybackDelay;
                if (enqueueDelay > TimeSpan.Zero)
                    await clock.Delay(enqueueDelay, cancellationToken).ConfigureAwait(false);
                await EnqueueEntry(entry, entrySkipTo, realtimeBeginsAt, cancellationToken).ConfigureAwait(false);
                realtimeBlockEnd = Moment.Max(realtimeBlockEnd, entryEndsAt + realtimeOffset);
            }
            MediaPlayer.Complete();
            await playTask.ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e,
                "Play({StartAt}) failed for chat #{ChatId} / {PlaybackKind}",
                startAt, ChatId, IsRealTimePlayer ? "real-time" : "historical");
            try {
                await MediaPlayer.Stop().ConfigureAwait(false);
            }
            catch {
                // Intended
            }
            throw;
        }
        finally {
            DebugLog?.LogInformation(
                "Play({StartAt}) stopped for chat #{ChatId} / {PlaybackKind}",
                startAt, ChatId, IsRealTimePlayer ? "real-time" : "historical");
        }
    }

    public Task Stop()
        => MediaPlayer.Stop();

    // Private  methods

    private async Task<Symbol> EnqueueEntry(
        ChatEntry audioEntry,
        TimeSpan skipTo,
        Moment? playAt,
        CancellationToken cancellationToken)
    {
        try {
            if (audioEntry.Type != ChatEntryType.Audio)
                throw new NotSupportedException($"The entry's Type must be {ChatEntryType.Audio}.");
            return audioEntry.IsStreaming
                ? await EnqueueStreamingEntry(audioEntry, skipTo, playAt, cancellationToken).ConfigureAwait(false)
                : await EnqueueNonStreamingEntry(audioEntry, skipTo, playAt, cancellationToken).ConfigureAwait(false);
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
        ChatEntry audioEntry, TimeSpan skipTo, Moment? playAt,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation(
            "Enqueuing streaming audio entry: chat #{ChatId}, entry #{EntryId}, stream #{StreamId}",
            audioEntry.ChatId,
            audioEntry.Id,
            audioEntry.StreamId);
        var trackId = MediaTrackId.GetAudioTrackId(audioEntry);
        var audio = await AudioSourceStreamer
            .GetAudio(audioEntry.StreamId, skipTo, cancellationToken)
            .ConfigureAwait(false);
        await MediaPlayer.AddMediaTrack(trackId,
                audio,
                audioEntry.BeginsAt,
                playAt,
                skipTo,
                cancellationToken)
            .ConfigureAwait(false);
        return trackId;
    }

    private async Task<Symbol> EnqueueNonStreamingEntry(
        ChatEntry audioEntry, TimeSpan skipTo, Moment? playAt,
        CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation(
            "Enqueuing non-streaming audio entry: chat #{ChatId}, entry #{EntryId}",
            audioEntry.ChatId,
            audioEntry.Id);
        var audioBlobUri = MediaResolver.GetAudioBlobUri(audioEntry);
        var audio = await AudioDownloader
            .Download(audioBlobUri, skipTo, cancellationToken)
            .ConfigureAwait(false);
        var trackId = MediaTrackId.GetAudioTrackId(audioEntry);
        await MediaPlayer.AddMediaTrack(
                trackId,
                audio,
                audioEntry.BeginsAt,
                playAt,
                skipTo,
                cancellationToken)
            .ConfigureAwait(false);
        return trackId;
    }
}
