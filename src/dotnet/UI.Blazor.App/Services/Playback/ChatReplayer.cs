using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Components;

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
        Playback playback,
        Moment minPlayAt,
        CancellationToken cancellationToken)
    {
        var rewindOffset = TimeSpan.Zero;
        var speed = 1.0d;
        // Read rewindOffset and speed from ReplayState (set by ChatAudioUI.StartReplay)
        var replayState = ChatAudioUI?.ReplayState.Value;
        if (replayState is { ChatId: var rsChatId } && rsChatId == ChatId) {
            speed = replayState.Speed;
            rewindOffset = replayState.RewindOffset;
        }
        var sleepDurationAtStart = SleepDuration.Value;
        var pauseDurationAtStart = Playback.TotalPauseDuration.Value;

        Log.LogInformation(
            "Starting server-streamed replay in chat {ChatId} from {MinPlayAt}, rewindOffset={RewindOffset}, speed={Speed}",
            ChatId, minPlayAt, rewindOffset, speed);
        var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        if (chat?.Rules.CanRead() != true) {
            Log.LogWarning("Cannot read chat {ChatId}", ChatId);
            return;
        }

        Operation = $"replaying in \"{chat.Title}\"";

        // TS-pull mode — see ChatListener.Play for caveats. Replay has no
        // WaitUntilReadyToPlay pacing in this path; the server-side replay
        // stream paces item delivery, so the TS renderer plays as frames
        // arrive.
        if (Hub.AudioSettings.UseTsAudioPull) {
            var bridge = Hub.Services.GetRequiredService<TsAudioPullBridge>();
            // Replay is historical playback — no self-echo concern for the
            // current user, but honour the same ListenOwnAudio debug flag as
            // the live path for consistency.
            var ownAuthorId = Constants.DebugMode.ListenOwnAudio
                ? (AuthorId?)null
                : (await Hub.Authors.GetOwn(Session, ChatId, cancellationToken).ConfigureAwait(false))?.Id;
            await using var handle = await bridge
                .StartReplay(Session, ChatId, minPlayAt, rewindOffset, speed, ownAuthorId, cancellationToken)
                .ConfigureAwait(false);
            try {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Expected
            }
            return;
        }

        var processor = new ReplayStreamProcessor(
            Hub.Services, Session, ChatId, minPlayAt, rewindOffset, speed,
            cancellationToken.CreateLinkedTokenSource());
        await using var _ = processor.ConfigureAwait(false);

        // Capture playbackStartedAt on first StreamStarted event, not before the RPC call,
        // to avoid wall-clock drift that would cause all tracks to play immediately.
        CpuTimestamp playbackStartedAt = default;
        var trackTasks = new ConcurrentBag<Task>();
        processor.StreamStarted += (info, playsAt, frames) => {
            if (playbackStartedAt == default)
                playbackStartedAt = CpuTimestamp.Now;
            var trackTask = OnStreamStarted(
                playback, info, playsAt, frames, speed,
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
        Playback playback,
        LiveStreamInfo streamInfo,
        TimeSpan playsAt,
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioFrames,
        double speed,
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
                        Speed = speed,
                    }
                    : new ChatAudioTrackInfo(ChatId, streamInfo.EntryId, chat, author) {
                        RecordedAt = streamInfo.BeginsAt,
                        ClientSideRecordedAt = streamInfo.BeginsAt,
                        Speed = speed,
                    };
                var playAt = Clocks.CpuClock.Now;
                var process = playback.Play(trackInfo, audioSource, playAt, cancellationToken);
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
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioFrames,
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
