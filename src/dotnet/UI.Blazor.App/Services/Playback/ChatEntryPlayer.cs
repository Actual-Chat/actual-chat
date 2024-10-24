using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.Messaging;
using ActualChat.Streaming;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class ChatEntryPlayer : ProcessorBase
{
    private ChatUIHub Hub { get; }
    private IStreamClient StreamClient => Hub.StreamClient;
    private AudioRecorder AudioRecorder => Hub.AudioRecorder;
    private AudioDownloader AudioDownloader => Hub.AudioDownloader;
    private AudioInitializer AudioInitializer => Hub.AudioInitializer;
    private MomentClockSet Clocks => Hub.Clocks();
    private UrlMapper UrlMapper => Hub.UrlMapper();
    private ILogger Log { get; }

    private HashSet<Task> EntryPlaybackTasks { get; } = new();

    public ChatId ChatId { get; }
    public Playback Playback { get; }

    public ChatEntryPlayer(
        ChatUIHub hub,
        ChatId chatId,
        Playback playback,
        CancellationToken cancellationToken)
        : base(cancellationToken.CreateLinkedTokenSource())
    {
        Hub = hub;
        ChatId = chatId;
        Playback = playback;
        Log = Hub.LogFor(GetType());
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
            await AudioInitializer.WhenInitialized.ConfigureAwait(false);

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
        var audioTask = StreamClient.GetAudio(audioEntry.StreamId, skipTo, cancellationToken);
        var chatTask = Hub.Chats.Get(Hub.Session(), audioEntry.ChatId, cancellationToken);
        var authorTask = Hub.Authors.Get(Hub.Session(), audioEntry.ChatId, audioEntry.AuthorId, cancellationToken);
        await Task.WhenAll(audioTask, chatTask, authorTask).ConfigureAwait(false);
        var audio = await audioTask.ConfigureAwait(false);
        var chat = await chatTask.ConfigureAwait(false);
        var author = await authorTask.ConfigureAwait(false);

        var trackInfo = new ChatAudioTrackInfo(audioEntry, chat!, author!) {
            RecordedAt = audioEntry.BeginsAt + skipTo,
            ClientSideRecordedAt = (audioEntry.ClientSideBeginsAt ?? audioEntry.BeginsAt) + skipTo,
        };
        var now = Clocks.SystemClock.Now;
        var latency = now - audio.CreatedAt;
        _ = BackgroundTask.Run(async () => {
            _ = StreamClient.ReportAudioLatency(latency, cancellationToken).ConfigureAwait(false);
            var recorderState = AudioRecorder.State.LastNonErrorValue;
            if (!recorderState.IsRecording || recorderState.ChatId != audioEntry.ChatId)
                return;

            var ownAuthor = await Hub.Authors.GetOwn(Hub.Session(), audioEntry.ChatId, cancellationToken).ConfigureAwait(false);
            if (audioEntry.AuthorId != (ownAuthor?.Id ?? AuthorId.None))
                _ = AudioRecorder.ConversationSignal(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
        return Playback.Play(trackInfo, audio, playAt, cancellationToken);
    }

    private async Task<IMessageProcess<PlayTrackCommand>> EnqueueNonStreamingEntry(
        ChatEntry audioEntry,
        TimeSpan skipTo,
        Moment playAt,
        CancellationToken cancellationToken)
    {
        var audioBlobUrl = UrlMapper.AudioBlobUrl(audioEntry);
        var audioTask = AudioDownloader.Download(audioBlobUrl, skipTo, cancellationToken);
        var chatTask = Hub.Chats.Get(Hub.Session(), audioEntry.ChatId, cancellationToken);
        var authorTask = Hub.Authors.Get(Hub.Session(), audioEntry.ChatId, audioEntry.AuthorId, cancellationToken);
        await Task.WhenAll(audioTask, chatTask, authorTask).ConfigureAwait(false);
        var audio = await audioTask.ConfigureAwait(false);
        var chat = await chatTask.ConfigureAwait(false);
        var author = await authorTask.ConfigureAwait(false);
        var trackInfo = new ChatAudioTrackInfo(audioEntry, chat!, author!) {
            RecordedAt = audioEntry.BeginsAt + skipTo,
            ClientSideRecordedAt = (audioEntry.ClientSideBeginsAt ?? audioEntry.BeginsAt) + skipTo,
        };
        return Playback.Play(trackInfo, audio, playAt, cancellationToken);
    }
}
