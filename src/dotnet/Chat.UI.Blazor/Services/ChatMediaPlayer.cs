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

    public bool MustWaitForNewEntries { get; init; } = false;
    public Session Session { get; init; } = Session.Null;
    public ChatId ChatId { get; init; } = default;

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

    private async Task Play(Moment startAt, CancellationToken cancellationToken)
    {
        await MediaPlayer.Stop().ConfigureAwait(false);
        var entryReader = Chats.CreateEntryReader(Session, ChatId);
        var startEntry = await entryReader
            .TryGet(startAt - ChatConstants.MaxEntryDuration, cancellationToken)
            .ConfigureAwait(false);
        var startEntryId = startEntry?.Id ?? 0;
        var maxPlayEnd = startAt - TimeSpan.FromMinutes(1);
        var entries = entryReader
            .GetAllAfter(startEntryId, MustWaitForNewEntries, cancellationToken)
            .Where(e => e.Type == ChatEntryType.Audio);

        await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false)) {
            // TODO(AY): Write this code
        }
    }

    private async Task PlayAudioEntry(ChatEntry audioEntry, TimeSpan offset, CancellationToken cancellationToken)
    {
        try {
            if (audioEntry.Type != ChatEntryType.Audio)
                throw new NotSupportedException($"The entry's Type must be {ChatEntryType.Audio}.");
            if (audioEntry.IsStreaming) {
                await PlayStreamingAudioEntry(audioEntry, cancellationToken).ConfigureAwait(false);
                return;
            }

            var audioBlobUri = MediaResolver.GetAudioBlobUri(audioEntry);
            var audioSource = await AudioDownloader.DownloadAsAudioSource(audioBlobUri, offset, cancellationToken);
            var trackId = ZString.Concat("audio:", audioEntry.ChatId, audioEntry.Id);
            await MediaPlayer.AddMediaTrack(trackId, audioSource, audioEntry.BeginsAt + offset, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e,
                "Error playing audio entry. ChatId = {ChatId}, ChatEntryId = {ChatEntryId}",
                audioEntry.ChatId,
                audioEntry.Id);
        }
    }

    private async Task PlayStreamingAudioEntry(ChatEntry audioEntry, CancellationToken cancellationToken)
    {
        try {
            if (audioEntry.Type != ChatEntryType.Audio)
                throw new NotSupportedException($"The entry's Type must be {ChatEntryType.Audio}.");
            if (!audioEntry.IsStreaming)
                throw new NotSupportedException("The entry must be a streaming entry.");

            var beginsAt = audioEntry.BeginsAt;
            var cutoffTime = Clocks.CpuClock.Now - TimeSpan.FromMinutes(1);
            var trackId = ZString.Concat("audio:", audioEntry.ChatId, audioEntry.Id);
            if (beginsAt < cutoffTime) return;

            var offset = beginsAt - cutoffTime;
            var audioSource = await AudioSourceStreamer.GetAudioSource(audioEntry.StreamId, offset, cancellationToken);
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
