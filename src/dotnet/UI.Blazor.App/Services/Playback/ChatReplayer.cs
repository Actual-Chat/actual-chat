using ActualChat.Audio;
using ActualChat.Live;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class ChatReplayer : ChatPlayer
{
    public ChatReplayer(AppUIHub hub, ChatId chatId)
        : base(hub, chatId)
        => PlayerKind = ChatPlayerKind.Replaying;

    public override void Pause()
    {
        _ = Playback.Pause(CancellationToken.None);
        if (ChatAudioUI!.ReplayState.Value is { } rs && rs.ChatId == ChatId)
            ChatAudioUI!.TryReleaseAudioFocus();
    }

    public override async Task Resume()
    {
        var replayState = ChatAudioUI!.ReplayState.Value;
        if (replayState is null || replayState.ChatId != ChatId) {
            Log.LogInformation("Can't resume replay. State: '{State}', ChatId: '{ChatId}'", replayState, ChatId);
            return;
        }

        if (!await ChatAudioUI!.TryAcquireAudioFocusForResume(this).ConfigureAwait(false))
            return;

        _ = Playback.Resume(default);
    }

    protected override async Task Play(
        ChatEntryPlayer entryPlayer,
        Moment minPlayAt,
        CancellationToken cancellationToken)
    {
        // Read offset from ReplayState (set by ChatAudioUI.StartReplay)
        var replayState = ChatAudioUI?.ReplayState.Value;
        var currentOffset = replayState is { ChatId: var rsChat } && rsChat == ChatId
            ? replayState.Offset
            : TimeSpan.Zero;

        var sleepDurationAtStart = SleepDuration.Value;
        var pauseDurationAtStart = Playback.TotalPauseDuration.Value;
        var playbackStartedAt = CpuTimestamp.Now;

        Log.LogInformation(
            "Starting server-streamed replay in chat {ChatId} from {MinPlayAt}, offset={Offset}",
            ChatId, minPlayAt, currentOffset);
        var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        if (chat?.Rules.CanRead() != true) {
            Log.LogWarning("Cannot read chat {ChatId}", ChatId);
            return;
        }

        Operation = $"replaying in \"{chat.Title}\"";
        var processor = new ReplayStreamProcessor(
            Hub.Services, Session, ChatId, minPlayAt, currentOffset,
            cancellationToken.CreateLinkedTokenSource());
        await using var _ = processor.ConfigureAwait(false);

        var trackTasks = new ConcurrentBag<Task>();
        processor.StreamStarted += (info, playsAt, frames) => {
            var trackTask = OnStreamStarted(
                entryPlayer, info, playsAt, frames,
                playbackStartedAt, sleepDurationAtStart, pauseDurationAtStart,
                cancellationToken);
            trackTasks.Add(trackTask);
        };

        // Wait for server to finish streaming all entries
        await processor.Run().ConfigureAwait(false);

        // Wait for all tracks to finish playing (but respect cancellation)
        await Task.WhenAll(trackTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task OnStreamStarted(
        ChatEntryPlayer entryPlayer,
        LiveStreamInfo streamInfo,
        TimeSpan playsAt,
        IAsyncEnumerable<byte[]> audioFrames,
        CpuTimestamp playbackStartedAt,
        TimeSpan sleepDurationAtStart,
        TimeSpan pauseDurationAtStart,
        CancellationToken cancellationToken)
        => BackgroundTask.Run(async () => {
            try {
                // Pace: wait until the right playback time
                await WaitUntilReadyToPlay(
                    playsAt, playbackStartedAt, sleepDurationAtStart, pauseDurationAtStart,
                    cancellationToken
                    ).ConfigureAwait(false);

                if (!await CanContinuePlayback(cancellationToken).ConfigureAwait(false))
                    return;

                // Create AudioSource from the stream frames
                var skipTo = TimeSpan.Zero; // Server already handles skip
                var audioSource = CreateAudioSource(streamInfo, audioFrames, skipTo, cancellationToken);

                // Get chat and author info for track metadata
                var chat = await Hub.Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
                if (chat == null)
                    return;

                var author = await Hub.Authors
                    .Get(Session, ChatId, streamInfo.AuthorId, cancellationToken)
                    .ConfigureAwait(false);

                // Look up ChatEntry from EntryId if available
                ChatEntry? audioEntry = null;
                if (streamInfo.EntryId is { } entryId) {
                    var entryReader = Hub.NewEntryReader(ChatId);
                    audioEntry = await entryReader.Get(entryId.LocalId, cancellationToken).ConfigureAwait(false);
                }

                var trackInfo = audioEntry != null
                    ? new ChatAudioTrackInfo(audioEntry, chat, author!) {
                        RecordedAt = streamInfo.BeginsAt,
                        ClientSideRecordedAt = streamInfo.BeginsAt,
                    }
                    : new ChatAudioTrackInfo(ChatId, streamInfo.EntryId, chat, author) {
                        RecordedAt = streamInfo.BeginsAt,
                        ClientSideRecordedAt = streamInfo.BeginsAt,
                    };
                var playAt = Clocks.CpuClock.Now;
                var process = entryPlayer.Playback.Play(trackInfo, audioSource, playAt, cancellationToken);
                await process.WhenCompleted.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Expected
            }
            catch (Exception e) {
                Log.LogWarning(e, "Error processing replay stream #{StreamId}", streamInfo.StreamId);
            }
        }, CancellationToken.None);

    private async Task WaitUntilReadyToPlay(
        TimeSpan playsAt,
        CpuTimestamp playbackStartedAt,
        TimeSpan sleepDurationAtStart,
        TimeSpan pauseDurationAtStart,
        CancellationToken cancellationToken)
    {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            await Playback.IsPaused.Computed
                .When(x => !x, cancellationToken)
                .ConfigureAwait(false);

            var wallElapsed = playbackStartedAt.Elapsed;
            var sleepDelta = SleepDuration.Value - sleepDurationAtStart;
            var pauseDelta = Playback.TotalPauseDuration.Value - pauseDurationAtStart;
            var playbackTime = wallElapsed - sleepDelta - pauseDelta;

            var delay = playsAt - playbackTime;
            if (delay <= TimeSpan.Zero)
                return;

            // Wait with awareness of device sleep (re-checks on wake)
            await Hub.DeviceAwakeUI
                .SleepUntil(Clocks.CpuClock, Clocks.CpuClock.Now + delay, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private AudioSource CreateAudioSource(
        LiveStreamInfo streamInfo,
        IAsyncEnumerable<byte[]> audioFrames,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var format = streamInfo.Format ?? AudioSource.DefaultFormat;
        var frameStream = audioFrames
            .Select((data, i) => new AudioFrame {
                Data = data,
                Offset = TimeSpan.FromMilliseconds(i * Constants.Audio.OpusFrameDurationMs),
                Duration = Constants.Audio.OpusFrameDuration,
            })
            .SkipWhile(f => f.Offset < skipTo)
            .Select(f => new AudioFrame {
                Data = f.Data,
                Offset = f.Offset - skipTo,
                Duration = f.Duration,
            });

        return new AudioSource(
            streamInfo.BeginsAt,
            format,
            frameStream,
            TimeSpan.Zero,
            Log,
            cancellationToken);
    }
}
