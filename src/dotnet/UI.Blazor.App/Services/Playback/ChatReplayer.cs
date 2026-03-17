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
        ChatEntryPlayer entryPlayer, Moment minPlayAt, CancellationToken cancellationToken)
    {
        // Read offset from ReplayState (set by ChatAudioUI.StartReplay)
        var replayState = ChatAudioUI?.ReplayState.Value;
        var currentOffset = replayState is { ChatId: var rsChat } && rsChat == ChatId
            ? replayState.Offset
            : TimeSpan.Zero;

        while (true) {
            var sleepDurationAtStart = SleepDuration.Value;
            var pauseDurationAtStart = Playback.TotalPauseDuration.Value;
            var playbackStartedAt = CpuTimestamp.Now;

            using var sleepCts = cancellationToken.CreateLinkedTokenSource();

            // Watch for sleep duration change in background → abort playback
            _ = BackgroundTask.Run(async () => {
                try {
                    await SleepDuration.Computed
                        .When(x => x != sleepDurationAtStart, cancellationToken)
                        .ConfigureAwait(false);
                    Log.LogInformation("Sleep detected, aborting replay for restart");
                    sleepCts.Cancel();
                }
                catch {
                    // Expected on normal completion
                }
            }, CancellationToken.None);

            try {
                await PlayCore(entryPlayer, minPlayAt, currentOffset,
                    playbackStartedAt, sleepDurationAtStart, pauseDurationAtStart,
                    sleepCts.Token).ConfigureAwait(false);
                return; // Normal completion
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                // Sleep triggered restart — compute how much audio time has elapsed
                var wallElapsed = playbackStartedAt.Elapsed;
                var sleepDelta = SleepDuration.Value - sleepDurationAtStart;
                var pauseDelta = Playback.TotalPauseDuration.Value - pauseDurationAtStart;
                var audioPlayed = (wallElapsed - sleepDelta - pauseDelta).Positive();

                Log.LogInformation(
                    "Restarting replay after sleep: audioPlayed={AudioPlayed}, sleepDelta={SleepDelta}",
                    audioPlayed, sleepDelta);

                // Abort current tracks
                await Playback.Abort().WhenCompleted.SilentAwait(false);

                // Advance offset by the amount of audio played
                currentOffset += audioPlayed;
            }
        }
    }

    private async Task PlayCore(
        ChatEntryPlayer entryPlayer,
        Moment minPlayAt,
        TimeSpan offset,
        CpuTimestamp playbackStartedAt,
        TimeSpan sleepDurationAtStart,
        TimeSpan pauseDurationAtStart,
        CancellationToken cancellationToken)
    {
        Log.LogInformation("Starting server-streamed replay in chat {ChatId} from {MinPlayAt}, offset={Offset}",
            ChatId, minPlayAt, offset);
        var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        if (chat?.Rules.CanRead() != true) {
            Log.LogWarning("Cannot read chat {ChatId}", ChatId);
            return;
        }

        Operation = $"replaying in \"{chat.Title}\"";

        var processor = new ReplayStreamProcessor(
            Hub.Services, Session, ChatId, minPlayAt, offset, cancellationToken.CreateLinkedTokenSource());
        await using var _ = processor.ConfigureAwait(false);

        var trackTasks = new ConcurrentBag<Task>();
        processor.StreamStarted += (info, playsAt, frames) => {
            var task = OnStreamStarted(entryPlayer, info, playsAt, frames,
                playbackStartedAt, sleepDurationAtStart, pauseDurationAtStart,
                cancellationToken);
            trackTasks.Add(task);
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
    {
        return BackgroundTask.Run(async () => {
            try {
                // Pace: wait until the right playback time
                await WaitUntilPlaybackTime(playsAt,
                    playbackStartedAt, sleepDurationAtStart, pauseDurationAtStart,
                    cancellationToken).ConfigureAwait(false);

                if (!await CanContinuePlayback(cancellationToken).ConfigureAwait(false))
                    return;

                // Create AudioSource from the stream frames
                var skipTo = TimeSpan.Zero; // Server already handles skip
                var audioSource = CreateAudioSource(streamInfo, audioFrames, skipTo, cancellationToken);

                // Get chat and author info for track metadata
                var chat = await Hub.Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
                var author = await Hub.Authors.Get(Session, ChatId, streamInfo.AuthorId, cancellationToken)
                    .ConfigureAwait(false);

                if (chat == null)
                    return;

                // Look up ChatEntry from EntryId if available
                ChatEntry? audioEntry = null;
                if (streamInfo.EntryId is { } entryId) {
                    var entryReader = Hub.NewEntryReader(ChatId);
                    audioEntry = await entryReader.Get(entryId.LocalId, cancellationToken).ConfigureAwait(false);
                }

                // Create track info
                ChatAudioTrackInfo trackInfo;
                if (audioEntry != null) {
                    trackInfo = new ChatAudioTrackInfo(audioEntry, chat, author!) {
                        RecordedAt = streamInfo.BeginsAt,
                        ClientSideRecordedAt = streamInfo.BeginsAt,
                    };
                }
                else {
                    trackInfo = new ChatAudioTrackInfo(ChatId, streamInfo.EntryId, chat, author) {
                        RecordedAt = streamInfo.BeginsAt,
                        ClientSideRecordedAt = streamInfo.BeginsAt,
                    };
                }

                var playAt = Clocks.CpuClock.Now;
                var process = entryPlayer.Playback.Play(trackInfo, audioSource, playAt, cancellationToken);
                // Wait for this track to finish playing
                await process.WhenCompleted.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Expected
            }
            catch (Exception e) {
                Log.LogWarning(e, "Error processing replay stream {StreamId}", streamInfo.StreamId);
            }
        }, CancellationToken.None);
    }

    private async Task WaitUntilPlaybackTime(
        TimeSpan playsAt,
        CpuTimestamp playbackStartedAt,
        TimeSpan sleepDurationAtStart,
        TimeSpan pauseDurationAtStart,
        CancellationToken cancellationToken)
    {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();

            // Wait for unpause first
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
