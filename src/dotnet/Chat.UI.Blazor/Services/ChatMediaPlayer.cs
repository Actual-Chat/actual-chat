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

    public Session Session { get; init; } = Session.Null;
    public ChatId ChatId { get; init; } = default;
    public bool IsRealTimePlayer { get; init; }
    public Option<AuthorId> SilencedAuthorIds { get; init; }
    public TimeSpan EnqueueToPlaybackDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public MediaPlayer MediaPlayer { get; }
    public bool IsPlaying => MediaPlayer.IsPlaying;

    public ChatMediaPlayer(IServiceProvider services)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        Log = services.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
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
                if (SilencedAuthorIds.IsSome(out var silencedAuthorId) && entry.AuthorId == silencedAuthorId)
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
                await EnqueuePlayback(entry, entrySkipTo, realtimeBeginsAt, cancellationToken).ConfigureAwait(false);
                realtimeBlockEnd = Moment.Max(realtimeBlockEnd, entryEndsAt + realtimeOffset);
            }
            await playTask.ConfigureAwait(false);
        }
        catch {
            try {
                await MediaPlayer.Stop().ConfigureAwait(false);
            }
            catch {
                // Intended
            }
            throw;
        }
    }

    public Task Stop()
        => MediaPlayer.Stop();

    // Private  methods

    private async Task<Symbol?> EnqueuePlayback(
        ChatEntry audioEntry,
        TimeSpan skipTo,
        Moment? playAt,
        CancellationToken cancellationToken)
    {
        try {
            if (audioEntry.Type != ChatEntryType.Audio)
                throw new NotSupportedException($"The entry's Type must be {ChatEntryType.Audio}.");

            if (audioEntry.IsStreaming)
                return IsRealTimePlayer
                    ? await EnqueueStreamingPlayback(audioEntry, skipTo, playAt, cancellationToken)
                        .ConfigureAwait(false)
                    : null;

            var audioBlobUri = MediaResolver.GetAudioBlobUri(audioEntry);
            var audioSource = await AudioDownloader.DownloadAsAudioSource(audioBlobUri, skipTo, cancellationToken)
                .ConfigureAwait(false);
            var trackId = MediaTrackId.GetAudioTrackId(audioEntry);
            await MediaPlayer.AddMediaTrack(trackId,
                    audioSource,
                    audioEntry.BeginsAt,
                    playAt,
                    skipTo,
                    cancellationToken)
                .ConfigureAwait(false);
            return trackId;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e,
                "Error playing audio entry. ChatId = {ChatId}, ChatEntryId = {ChatEntryId}",
                audioEntry.ChatId,
                audioEntry.Id);
        }
        return null;
    }

    private async Task<Symbol?> EnqueueStreamingPlayback(
        ChatEntry audioEntry,
        TimeSpan skipTo,
        Moment? playAt,
        CancellationToken cancellationToken)
    {
        try {
            if (audioEntry.Type != ChatEntryType.Audio)
                throw new NotSupportedException($"The entry's Type must be {ChatEntryType.Audio}.");
            if (!audioEntry.IsStreaming)
                throw new NotSupportedException("The entry must be a streaming entry.");

            var trackId = MediaTrackId.GetAudioTrackId(audioEntry);
            var audioSource = await AudioSourceStreamer.GetAudioSource(audioEntry.StreamId, skipTo, cancellationToken)
                .ConfigureAwait(false);
            await MediaPlayer.AddMediaTrack(trackId,
                    audioSource,
                    audioEntry.BeginsAt,
                    playAt,
                    skipTo,
                    cancellationToken)
                .ConfigureAwait(false);
            return trackId;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e,
                "Error playing streaming audio entry. ChatId = {ChatId}, ChatEntryId = {ChatEntryId}, StreamId = {StreamId}",
                audioEntry.ChatId,
                audioEntry.Id,
                audioEntry.StreamId);
        }
        return null;
    }
}
