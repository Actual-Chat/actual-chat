using ActualChat.Audio;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.MediaPlayback;
using ActualChat.Messaging;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatEntryPlayer : ProcessorBase
{
    private IServiceProvider Services { get; }
    private AudioRecorder AudioRecorder { get; }
    private MomentClockSet Clocks { get; }
    private UrlMapper UrlMapper { get; }
    private ILogger Log { get; }

    private AudioDownloader AudioDownloader { get; }
    private IAudioStreamer AudioStreamer { get; }

    private HashSet<Task> EntryPlaybackTasks { get; } = new();

    public Session Session { get; }
    public ChatId ChatId { get; }
    public Playback Playback { get; }

    public ChatEntryPlayer(
        Session session,
        ChatId chatId,
        Playback playback,
        IServiceProvider services,
        CancellationToken cancellationToken)
        : base(cancellationToken.CreateLinkedTokenSource())
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
        AudioRecorder = services.GetRequiredService<AudioRecorder>();
    }

    protected override Task DisposeAsyncCore()
        => Abort(); // Never throws

    public async Task WhenDonePlaying()
    {
        while (true) {
            List<Task> entryPlaybackTasks;
            lock (Lock) {
                if (EntryPlaybackTasks.Count == 0)
                    return;
                entryPlaybackTasks = EntryPlaybackTasks.ToList();
            }
            await Task.WhenAll(entryPlaybackTasks).ConfigureAwait(false);
        }
    }

    public void EnqueueEntry(ChatEntry entry, TimeSpan skipTo, Moment? playAt = null)
    {
        var resultSource = TaskCompletionSourceExt.New<Unit>();
        lock (Lock) {
            if (StopToken.IsCancellationRequested)
                return; // This entry starting after Dispose or Abort
            EntryPlaybackTasks.Add(resultSource.Task);
        }

        _ = BackgroundTask.Run(async () => {
            try {
                var playProcess = await EnqueueEntry(entry, skipTo, playAt ?? Clocks.CpuClock.Now, StopToken).ConfigureAwait(false);
                await playProcess.WhenCompleted.ConfigureAwait(false);
            }
            catch (Exception e) {
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

    public async Task Abort()
    {
        try {
            await Playback.Abort().WhenCompleted.ConfigureAwait(false);
        }
        catch (Exception e) {
            if (e is not OperationCanceledException or ObjectDisposedException)
                Log.LogError(e, "Failed to abort playback in chat #{ChatId}", ChatId);
        }
    }

    // Private methods

    private async Task<IMessageProcess<PlayTrackCommand>> EnqueueEntry(
        ChatEntry audioEntry,
        TimeSpan skipTo,
        Moment playAt,
        CancellationToken cancellationToken)
    {
        try {
            var audioInitializer = Services.GetRequiredService<AudioInitializer>();
            await audioInitializer.WhenInitialized.ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (audioEntry.Kind != ChatEntryKind.Audio)
                throw StandardError.NotSupported($"The entry's Type must be {ChatEntryKind.Audio}.");
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
        _ = BackgroundTask.Run(async () => {
                var now = Clocks.SystemClock.Now;
                var latency = now - audio.CreatedAt;
                await AudioStreamer.ReportLatency(latency, cancellationToken).ConfigureAwait(false);
                var recorderState = AudioRecorder.State.LastNonErrorValue;
                if (recorderState.IsRecording && recorderState.ChatId == audioEntry.ChatId)
                    await AudioRecorder.ConversationSignal(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
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
