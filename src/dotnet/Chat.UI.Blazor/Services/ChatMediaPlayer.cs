using ActualChat.Audio;
using ActualChat.Playback;
using Cysharp.Text;
using Microsoft.Extensions.DependencyInjection;
using Stl.Fusion.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatMediaPlayer
{
    private IChatService Chats { get; }
    private MediaPlayer MediaPlayer { get; }
    private IChatMediaResolver MediaResolver { get; }
    private AudioDownloader AudioDownloader { get; }
    private IAudioSourceStreamer AudioSourceStreamer { get; }
    private MomentClockSet Clocks { get; }
    private ILogger Log { get; }

    public Session Session { get; init; } = Session.Null;
    public ChatId ChatId { get; init; } = default;
    public bool MustWaitForNewEntries { get; init; } = false;
    public TimeSpan EnqueueToPlaybackDelay { get; init; } = TimeSpan.FromSeconds(1);

    public ChatMediaPlayer(IChatService chats)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        var services = ((IComputeService) chats).GetServiceProvider();
        Log = services.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
        Chats = chats;
        MediaPlayer = services.GetRequiredService<MediaPlayer>();
        MediaResolver = services.GetRequiredService<IChatMediaResolver>();
        AudioDownloader = services.GetRequiredService<AudioDownloader>();
        AudioSourceStreamer = services.GetRequiredService<IAudioSourceStreamer>();
        Clocks = services.Clocks();
    }

    public async Task Play(Moment startAt, CancellationToken cancellationToken)
    {
        await MediaPlayer.Stop().ConfigureAwait(false);
        await MediaPlayer.Play().ConfigureAwait(false);
        try {
            var clock = Clocks.CpuClock;
            var entryReader = Chats.CreateEntryReader(Session, ChatId);
            var startEntry = await entryReader
                .TryGet(startAt - ChatConstants.MaxEntryDuration, cancellationToken)
                .ConfigureAwait(false);
            var startEntryId = startEntry?.Id ?? 0;
            var now = clock.Now;
            var continuousSpanEndTime = now - ChatConstants.MaxEntryDuration; // Should be far away in past
            var realtimeGap = now - startAt;

            var entries = entryReader
                .GetAllAfter(startEntryId, MustWaitForNewEntries, cancellationToken)
                .Where(e => e.Type == ChatEntryType.Audio);
            await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (entry.EndsAt < startAt) {
                    // We're normally starting @ (startAt - ChatConstants.MaxEntryDuration),
                    // so we need to skip a few entries.
                    // Note that streaming entries have EndsAt == null, so we don't skip them.
                    continue;
                }

                now = clock.Now;
                var beginsAt = Moment.Max(entry.BeginsAt, startAt);
                if (entry.BeginsAt > continuousSpanEndTime) {
                    // There is a gap between the currently playing "block" and the entry;
                    // we're going to skip the gap here by adjusting the realtime gap.
                    realtimeGap = now - beginsAt;
                }

                var skipTo = beginsAt - entry.BeginsAt;
                var enqueueDelay = beginsAt + realtimeGap - EnqueueToPlaybackDelay - now;
                continuousSpanEndTime = Moment.Max(continuousSpanEndTime, entry.EndsAt ?? now);
                await clock.Delay(enqueueDelay, cancellationToken).ConfigureAwait(false);
                await EnqueuePlayback(entry, skipTo, cancellationToken).ConfigureAwait(false);
            }
            MediaPlayer.Complete();
            await MediaPlayer.PlayingTask.ConfigureAwait(false);
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

    private async Task EnqueuePlayback(ChatEntry audioEntry, TimeSpan skipTo, CancellationToken cancellationToken)
    {
        try {
            if (audioEntry.Type != ChatEntryType.Audio)
                throw new NotSupportedException($"The entry's Type must be {ChatEntryType.Audio}.");
            if (audioEntry.IsStreaming) {
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

    private async Task EnqueueStreamingPlayback(ChatEntry audioEntry, TimeSpan skipTo, CancellationToken cancellationToken)
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
