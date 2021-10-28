using ActualChat.Audio;
using ActualChat.Playback;
using Cysharp.Text;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.Chat.UI.Blazor.Services;

public abstract class ChatMediaPlayer : IDisposable
{
    private IChatMediaResolver MediaResolver { get; }
    private AudioDownloader AudioDownloader { get; }
    private IAudioSourceStreamer AudioSourceStreamer { get; }

    protected IChatService Chats { get; }
    protected IMediaPlayerService MediaPlayerService { get; }
    protected MomentClockSet Clocks { get; }
    protected ILogger Log { get; }

    public MediaPlayer MediaPlayer { get; }
    public Session Session { get; init; } = Session.Null;
    public ChatId ChatId { get; init; } = default;
    public abstract bool IsRealTimePlayer { get; }
    public bool IsPlaying => MediaPlayer.IsPlaying;

    protected ChatMediaPlayer(IServiceProvider services)
    {
        // ReSharper disable once SuspiciousTypeConversion.Global
        Log = services.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
        Chats = services.GetRequiredService<IChatService>();
        MediaPlayer = services.GetRequiredService<MediaPlayer>();
        MediaResolver = services.GetRequiredService<IChatMediaResolver>();
        AudioDownloader = services.GetRequiredService<AudioDownloader>();
        AudioSourceStreamer = services.GetRequiredService<IAudioSourceStreamer>();
        MediaPlayerService = services.GetRequiredService<IMediaPlayerService>();
        Clocks = services.Clocks();
    }

    public void Dispose()
        => MediaPlayer.Dispose();

    public Task Stop()
        => MediaPlayer.Stop();

    // Protected  methods

    protected async Task<Symbol?> EnqueuePlayback(
        ChatEntry audioEntry,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        try {
            if (audioEntry.Type != ChatEntryType.Audio)
                throw new NotSupportedException($"The entry's Type must be {ChatEntryType.Audio}.");

            if (audioEntry.IsStreaming) {
                if (IsRealTimePlayer)
                    return await EnqueueStreamingPlayback(audioEntry, skipTo, cancellationToken).ConfigureAwait(false);

                return null;
            }

            var audioBlobUri = MediaResolver.GetAudioBlobUri(audioEntry);
            var audioSource = await AudioDownloader.DownloadAsAudioSource(audioBlobUri, skipTo, cancellationToken);
            var trackId = (Symbol)ZString.Concat("audio:", audioEntry.ChatId, audioEntry.Id);
            await MediaPlayer.AddMediaTrack(trackId, audioSource, audioEntry.BeginsAt + skipTo, cancellationToken);
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

    protected async Task<Symbol?> EnqueueStreamingPlayback(
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
            var trackId = (Symbol)ZString.Concat("audio:", audioEntry.ChatId, audioEntry.Id);
            var audioSource = await AudioSourceStreamer.GetAudioSource(audioEntry.StreamId, skipTo, cancellationToken);
            await MediaPlayer.AddMediaTrack(trackId, audioSource, beginsAt, cancellationToken);
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
