using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.Messaging;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatEntryPlayer : ProcessorBase
{
    private IServiceProvider Services { get; }
    private MomentClockSet Clocks { get; }
    private UrlMapper UrlMapper { get; }
    private ILogger Log { get; }

    private AudioDownloader AudioDownloader { get; }
    private IAudioStreamer AudioStreamer { get; }

    private HashSet<Task> EntryPlaybackTasks { get; } = new();
    private CancellationTokenSource AbortTokenSource { get; }
    private CancellationToken AbortToken { get; }

    public Session Session { get; }
    public Symbol ChatId { get; }
    public Playback Playback { get; }

    public ChatEntryPlayer(
        Session session,
        Symbol chatId,
        Playback playback,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        Session = session;
        ChatId = chatId;
        Playback = playback;
        Services = services;

        Log = services.LogFor(GetType());
        Clocks = services.Clocks();
        UrlMapper = services.GetRequiredService<UrlMapper>();
        AudioDownloader = services.GetRequiredService<AudioDownloader>();
        AudioStreamer = services.GetRequiredService<IAudioStreamer>();

        AbortTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        AbortToken = AbortTokenSource.Token;
    }

    protected override async Task DisposeAsyncCore()
    {
        // Default scheduler is used from here

        // This method starts inside 'lock (Lock)' block - see ProcessorBase.DisposeAsync,
        // so we don't need to acquire this lock here to access EntryPlaybackTasks.
        var entryPlaybackTasks = EntryPlaybackTasks.ToList();
        try {
            await Task.WhenAll(entryPlaybackTasks).ConfigureAwait(false);
        }
        finally {
            Abort();
            if (Playback.IsPlaying.Value) {
                var stopProcess = Playback.Stop(CancellationToken.None);
                try {
                    await stopProcess.WhenCompleted.ConfigureAwait(false);
                }
                catch (Exception e) {
                    if (e is not OperationCanceledException)
                        Log.LogError(e, "Failed to stop playback in chat #{ChatId}", ChatId);
                }
            }
        }
    }

    public void EnqueueEntry(ChatEntry entry, TimeSpan skipTo, Moment? playAt = null)
    {
        var resultSource = TaskSource.New<Unit>(true);
        lock (Lock) {
            if (StopToken.IsCancellationRequested || AbortToken.IsCancellationRequested)
                return; // This entry starting after Dispose or Abort
            EntryPlaybackTasks.Add(resultSource.Task);
        }

        BackgroundTask.Run(async () => {
            try {
                var playProcess = await EnqueueEntry(entry, skipTo, playAt ?? Clocks.CpuClock.Now, AbortToken).ConfigureAwait(false);
                await playProcess.WhenCompleted.ConfigureAwait(false);
            }
            catch (Exception e) {
                Console.WriteLine(e);
                if (e is not OperationCanceledException)
                    Log.LogError(e, "Entry playback failed in chat #{ChatId}, entry #{EntryId}", ChatId, entry.Id);
            }
            finally {
                resultSource.TrySetResult(default);
                lock (Lock)
                    EntryPlaybackTasks.Remove(resultSource.Task);
            }
        }, CancellationToken.None);
    }

    public void Abort()
        => AbortTokenSource.CancelAndDisposeSilently();

    // Private methods

    private async Task<IMessageProcess<PlayTrackCommand>> EnqueueEntry(
        ChatEntry audioEntry,
        TimeSpan skipTo,
        Moment playAt,
        CancellationToken cancellationToken)
    {
        try {
            cancellationToken.ThrowIfCancellationRequested();
            if (audioEntry.Type != ChatEntryType.Audio)
                throw StandardError.NotSupported($"The entry's Type must be {ChatEntryType.Audio}.");
            if (audioEntry.Duration is { } duration && skipTo.TotalSeconds > duration)
                return PlayTrackCommand.PlayNothingProcess;
            return await (audioEntry.IsStreaming
                ? EnqueueStreamingEntry(audioEntry, skipTo, playAt, cancellationToken)
                : EnqueueNonStreamingEntry(audioEntry, skipTo, playAt, cancellationToken)
                ).ConfigureAwait(false);
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

    private async Task<IMessageProcess<PlayTrackCommand>> EnqueueStreamingEntry(
        ChatEntry audioEntry,
        TimeSpan skipTo,
        Moment playAt,
        CancellationToken cancellationToken)
    {
        var audio = await AudioStreamer
            .GetAudio(audioEntry.StreamId, skipTo, cancellationToken)
            .ConfigureAwait(false);
        var trackInfo = new ChatAudioTrackInfo(audioEntry) {
            RecordedAt = audioEntry.BeginsAt + skipTo,
            ClientSideRecordedAt = (audioEntry.ClientSideBeginsAt ?? audioEntry.BeginsAt) + skipTo,
        };
        return Playback.Play(trackInfo, audio, playAt, cancellationToken);
    }

    private async Task<IMessageProcess<PlayTrackCommand>> EnqueueNonStreamingEntry(
        ChatEntry audioEntry,
        TimeSpan skipTo,
        Moment playAt,
        CancellationToken cancellationToken)
    {
        var audioBlobUrl = UrlMapper.AudioBlobUrl(audioEntry);
        var audio = await AudioDownloader
            .Download(audioBlobUrl, skipTo, cancellationToken)
            .ConfigureAwait(false);
        var trackInfo = new ChatAudioTrackInfo(audioEntry) {
            RecordedAt = audioEntry.BeginsAt + skipTo,
            ClientSideRecordedAt = (audioEntry.ClientSideBeginsAt ?? audioEntry.BeginsAt) + skipTo,
        };
        return Playback.Play(trackInfo, audio, playAt, cancellationToken);
    }
}
