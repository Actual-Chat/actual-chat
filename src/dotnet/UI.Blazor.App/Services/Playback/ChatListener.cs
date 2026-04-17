using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class ChatListener : ChatPlayer
{
    public ChatListener(AppUIHub hub, ChatId chatId)
        : base(hub, chatId)
        => PlayerKind = ChatPlayerKind.Listening;

    public override void Pause()
    {
        _ = Playback.Pause(CancellationToken.None);
        ChatAudioUI!.TryReleaseAudioFocus();
    }

    public override async Task Resume()
    {
        var listeningChatIds = await ChatAudioUI!.GetListeningChatIds().ConfigureAwait(false);
        if (!listeningChatIds.Contains(ChatId)) {
            Log.LogInformation("Can't resume listening playback. ChatId '{ChatId}' not in listening set", ChatId);
            return;
        }

        if (!await ChatAudioUI!.TryAcquireAudioFocusForResume(this).ConfigureAwait(false))
            return;

        _ = Playback.Resume(default);
    }

    protected override async Task Play(
        Playback playback, Moment minPlayAt, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        if (chat == null || !chat.Rules.CanRead())
            return;

        var serverClock = Clocks.ServerClock;
        await serverClock.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);

        Operation = $"listening in \"{chat.Title}\"";
        DebugLog?.LogDebug("Play: {ChatId}, {StartedAt}", ChatId, minPlayAt);

        var state = new PlayState {
            SyncedSleepDuration = SleepDuration.Value,
            MinPlayAt = serverClock.Now,
            LastStreamBeginsAt = serverClock.Now,
        };

        // Connect to Live Hub with automatic reconnection
        var settings = LiveStreamSettings.Default;
        var processor = new LiveStreamProcessor(
            Hub.Services, Session, ChatId, settings, cancellationToken.CreateLinkedTokenSource());
        await using var _ = processor.ConfigureAwait(false);

        processor.StreamStarted +=
            (info, _, frames) => OnStreamStarted(playback, state, info, frames, cancellationToken);
        await processor.Run().ConfigureAwait(false);
    }

    private void OnStreamStarted(
        Playback playback,
        PlayState state,
        LiveStreamInfo streamInfo,
        IAsyncEnumerable<ReadOnlyMemory<byte>> audioFrames,
        CancellationToken cancellationToken)
    {
        _ = BackgroundTask.Run(async () => {
            var serverClock = Clocks.ServerClock;
            try {
                // Skip own audio unless in debug mode
                if (!Constants.DebugMode.ListenOwnAudio) {
                    var author = await Authors.GetOwn(Session, ChatId, cancellationToken)
                        .ConfigureAwait(false);
                    if (author != null && streamInfo.AuthorId == author.Id)
                        return;
                }

                if (!await CanContinuePlayback(cancellationToken).ConfigureAwait(false)) {
                    await ChatAudioUI!.SetListeningState(ChatId, false).ConfigureAwait(false);
                    return;
                }

                // Check for sleep drift
                var sleepDuration = SleepDuration.Value;
                var minPlayAt = state.MinPlayAt;
                if (sleepDuration - state.SyncedSleepDuration > Constants.Audio.MaxRealtimeStreamDrift) {
                    minPlayAt = serverClock.Now - Constants.Audio.MaxRealtimeStreamDrift;
                    state.MinPlayAt = minPlayAt;
                    state.SyncedSleepDuration = sleepDuration;
                }

                var playAt = Moment.Max(minPlayAt, streamInfo.BeginsAt);
                if (playAt >= streamInfo.BeginsAt + Constants.Chat.MaxEntryDuration)
                    return;

                // Play notification sound for new message after delay
                if (streamInfo.BeginsAt - state.LastStreamBeginsAt > Hub.AudioSettings.IdleListeningNewMessageTrigger)
                    await Hub.TuneUI.PlayAndWait(Tune.NotifyOnNewAudioMessageAfterDelay).ConfigureAwait(false);

                state.LastStreamBeginsAt = streamInfo.BeginsAt;

                // Report end-to-end audio latency
                var latency = serverClock.Now - streamInfo.BeginsAt;
                _ = Hub.StreamClient.ReportAudioLatency(latency, cancellationToken).ConfigureAwait(false);

                // Create AudioSource from the stream frames
                var skipTo = (playAt - streamInfo.BeginsAt).Positive();
                var audioSource = CreateAudioSource(streamInfo, audioFrames, skipTo, cancellationToken);

                // Enqueue for playback
                DebugLog?.LogDebug("Play: enqueuing stream #{StreamId} @ {SkipTo}",
                    streamInfo.StreamId, skipTo.ToShortString());

                await EnqueueAudioSource(playback, streamInfo, audioSource, playAt, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Expected
            }
            catch (Exception e) {
                Log.LogWarning(e, "Error processing stream #{StreamId}", streamInfo.StreamId);
            }
        }, cancellationToken);
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

    private async Task EnqueueAudioSource(
        Playback playback,
        LiveStreamInfo streamInfo,
        AudioSource audioSource,
        Moment playAt,
        CancellationToken cancellationToken)
    {
        // Get chat and author info for track metadata
        var chat = await Hub.Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        var author = await Hub.Authors.Get(Session, ChatId, streamInfo.AuthorId, cancellationToken).ConfigureAwait(false);

        if (chat == null)
            return;

        // Create track info for live stream (no entry ID available yet)
        var trackInfo = new ChatAudioTrackInfo(ChatId, null, chat, author) {
            RecordedAt = streamInfo.BeginsAt,
            ClientSideRecordedAt = streamInfo.BeginsAt,
        };

        playback.Play(trackInfo, audioSource, playAt, cancellationToken);
    }

    // Nested types

    private sealed class PlayState
    {
        public TimeSpan SyncedSleepDuration { get; set; }
        public Moment MinPlayAt { get; set; }
        public Moment LastStreamBeginsAt { get; set; }
    }
}
