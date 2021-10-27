using ActualChat.Audio;
using ActualChat.Playback;
using Cysharp.Text;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatMediaPlayer : IDisposable
{
    private IChatService Chats { get; }
    private IChatMediaResolver MediaResolver { get; }
    private AudioDownloader AudioDownloader { get; }
    private IAudioSourceStreamer AudioSourceStreamer { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public MediaPlayer MediaPlayer { get; }
    public Session Session { get; init; } = Session.Null;
    public ChatId ChatId { get; init; } = default;
    public bool IsRealTimePlayer { get; init; } = false;
    public TimeSpan EnqueueToPlaybackDelay { get; init; } = TimeSpan.FromSeconds(0.2);

    public bool IsPlaying => MediaPlayer.IsPlaying;

    public ChatMediaPlayer(IServiceProvider services)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        Log = services.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
        Chats = services.GetRequiredService<IChatService>();
        MediaPlayer = services.GetRequiredService<MediaPlayer>();
        MediaResolver = services.GetRequiredService<IChatMediaResolver>();
        AudioDownloader = services.GetRequiredService<AudioDownloader>();
        AudioSourceStreamer = services.GetRequiredService<IAudioSourceStreamer>();
        Clocks = services.Clocks();
    }

    public void Dispose()
        => MediaPlayer.Dispose();

    public async Task Play(Moment startAt)
    {
        await MediaPlayer.Stop().ConfigureAwait(false);
        var playTask = MediaPlayer.Play();
        var cancellationToken = MediaPlayer.StopToken;

        try {
            var clock = Clocks.CpuClock;
            var entryReader = Chats.CreateEntryReader(Session, ChatId);
            var priorEntriesOffset = IsRealTimePlayer
                ? TimeSpan.Zero
                : ChatConstants.MaxEntryDuration;
            var startEntryId = await entryReader
                .GetNextEntryId(startAt - priorEntriesOffset, cancellationToken)
                .ConfigureAwait(false);
            var now = clock.Now;
            var continuousSpanEndTime = now - priorEntriesOffset; // Should be far away in past
            var realtimeGap = now - startAt;

            var entries = entryReader
                .GetAllAfter(startEntryId, IsRealTimePlayer, cancellationToken)
                .Where(e => e.Type == ChatEntryType.Audio);
            await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (entry.EndsAt < startAt)
                    // We're normally starting @ (startAt - ChatConstants.MaxEntryDuration),
                    // so we need to skip a few entries.
                    // Note that streaming entries have EndsAt == null, so we don't skip them.
                    continue;

                now = clock.Now;
                var beginsAt = Moment.Max(entry.BeginsAt, startAt);
                if (entry.BeginsAt > continuousSpanEndTime)
                    // There is a gap between the currently playing "block" and the entry;
                    // we're going to skip the gap here by adjusting the realtime gap.
                    realtimeGap = now - beginsAt;

                var skipTo = beginsAt - entry.BeginsAt;
                var enqueueDelay = beginsAt + realtimeGap - EnqueueToPlaybackDelay - now;
                continuousSpanEndTime = Moment.Max(continuousSpanEndTime, entry.EndsAt ?? now);
                if (enqueueDelay > TimeSpan.Zero)
                    await clock.Delay(enqueueDelay, cancellationToken).ConfigureAwait(false);
                await EnqueuePlayback(entry, skipTo, cancellationToken).ConfigureAwait(false);
            }
            MediaPlayer.Complete();
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

    // Private methods

    private async Task EnqueuePlayback(ChatEntry audioEntry, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        try {
            if (audioEntry.Type != ChatEntryType.Audio)
                throw new NotSupportedException($"The entry's Type must be {ChatEntryType.Audio}.");

            if (audioEntry.IsStreaming) {
                if (IsRealTimePlayer)
                    await EnqueueStreamingPlayback(audioEntry, skipTo, cancellationToken).ConfigureAwait(false);
                return;
            }

            var audioBlobUri = MediaResolver.GetAudioBlobUri(audioEntry);
            var audioSource = await AudioDownloader.DownloadAsAudioSource(audioBlobUri, skipTo, cancellationToken);
            var trackId = ZString.Concat("audio:", audioEntry.ChatId, audioEntry.Id);
            await MediaPlayer.AddMediaTrack(trackId, audioSource, audioEntry.BeginsAt + skipTo, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e,
                "Error playing audio entry. ChatId = {ChatId}, ChatEntryId = {ChatEntryId}",
                audioEntry.ChatId,
                audioEntry.Id);
        }
    }

    private async Task EnqueueStreamingPlayback(
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        try {
            if (audioEntry.Type != ChatEntryType.Audio)
                throw new NotSupportedException($"The entry's Type must be {ChatEntryType.Audio}.");
            if (!audioEntry.IsStreaming)
                throw new NotSupportedException("The entry must be a streaming entry.");

            var beginsAt = audioEntry.BeginsAt;
            var trackId = ZString.Concat("audio:", audioEntry.ChatId, audioEntry.Id);
            var audioSource = await AudioSourceStreamer.GetAudioSource(audioEntry.StreamId, skipTo, cancellationToken);
            await MediaPlayer.AddMediaTrack(trackId, audioSource, beginsAt, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e,
                "Error playing streaming audio entry. ChatId = {ChatId}, ChatEntryId = {ChatEntryId}, StreamId = {StreamId}",
                audioEntry.ChatId,
                audioEntry.Id,
                audioEntry.StreamId);
        }
    }
}
