using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class ChatListeningPlayer : ChatPlayer
{
    public ChatListeningPlayer(AppUIHub hub, ChatId chatId)
        : base(hub, chatId)
        => PlayerKind = ChatPlayerKind.Listening;

    public override void Pause()
    {
        _ = Playback.Pause(CancellationToken.None);
        ChatAudioUI.TryReleaseAudioFocus();
    }

    public override async Task Resume()
    {
        var listeningChatIds = await ChatAudioUI.GetListeningChatIds().ConfigureAwait(false);
        if (!listeningChatIds.Contains(ChatId)) {
            Log.LogInformation("Can't resume listening playback. ChatId '{ChatId}' not in listening set", ChatId);
            return;
        }

        if (!await ChatAudioUI.TryAcquireAudioFocusForResume(this).ConfigureAwait(false))
            return;

        _ = Playback.Resume(default);
    }

    protected override async Task Play(
        Playback playback,
        Moment startAt, // Server time
        CancellationToken cancellationToken)
    {
        var chat = await GetChat(cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return;

        DebugLog?.LogDebug("Listening in #{ChatId} @ {StartAt}", ChatId, startAt);
        Operation = $"listening in \"{chat.Title}\"";
        var state = new PlayState(startAt, SleepDuration.Value);

        // Connect to Live Hub with automatic reconnection
        var streamProcessor = new ListeningStreamProcessor(
            Hub.Services, Session, ChatId, cancellationToken.CreateLinkedTokenSource());
        await using var _ = streamProcessor.ConfigureAwait(false);

        streamProcessor.StreamStarted +=
            (info, _, frames) => OnStreamStarted(playback, state, info, frames, cancellationToken);
        await streamProcessor.Run().ConfigureAwait(false);
    }

    private void OnStreamStarted(
        Playback playback,
        PlayState state,
        LiveAudioStreamInfo streamInfo,
        IAsyncEnumerable<AudioFrame> audioFrames,
        CancellationToken cancellationToken)
    {
        _ = BackgroundTask.Run(async () => {
            var serverClock = Clocks.ServerClock;
            try {
                // Skip own audio unless in debug mode
                if (!Constants.DebugMode.ListenOwnAudio) {
                    var author = await Authors.GetOwn(Session, ChatId, cancellationToken).ConfigureAwait(false);
                    if (author != null && streamInfo.AuthorId == author.Id)
                        return;
                }

                if (!await CanContinuePlayback(cancellationToken).ConfigureAwait(false)) {
                    await ChatAudioUI.SetListeningState(ChatId, false).ConfigureAwait(false);
                    return;
                }

                // Check for sleep drift
                Moment startAt;
                lock (state.Lock) {
                    startAt = state.StartAt;
                    var sleepDuration = SleepDuration.Value;
                    if (sleepDuration != state.LastSleepDuration)
                        state.Reset(startAt = serverClock.Now, sleepDuration);
                    if (streamInfo.BeginsAt - state.LastNotifyTunePlayedAt > Hub.AudioSettings.IdleListeningNewMessageTrigger) {
                        _ = Hub.TuneUI.Play(Tune.NotifyOnNewAudioMessageAfterDelay);
                        state.LastNotifyTunePlayedAt = streamInfo.BeginsAt;
                    }
                }

                var playbackTargetBufferSize = await ChatAudioUI
                    .GetPlaybackTargetBufferSize(ChatId, cancellationToken) // Changes dependently on video presence
                    .ConfigureAwait(false);
                var minPlayAt = Moment.Max(startAt, serverClock.Now - playbackTargetBufferSize);
                var playAt = Moment.Max(streamInfo.BeginsAt, minPlayAt);
                if (playAt >= streamInfo.BeginsAt + Constants.Chat.MaxEntryDuration)
                    return;

                // Report end-to-end audio latency
                _ = Hub.LiveAudioStreams
                    .ReportAudioLatency(Hub.Session, serverClock.Now - streamInfo.BeginsAt, cancellationToken)
                    .ConfigureAwait(false);

                // The server muxer trims stale audio only at GetMuxedStream time;
                // a long-lived muxed stream keeps delivering frames, so we still skip
                // the backlog here to catch up after a device sleep (playAt jumps to
                // serverClock.Now once sleep drift resets state.StartAt).
                var skipTo = (playAt - streamInfo.BeginsAt).Positive();
                var audioSource = CreateAudioSource(streamInfo, audioFrames, skipTo, cancellationToken);

                // Enqueue for playback
                DebugLog?.LogDebug("Play: enqueuing stream #{StreamId} @ {SkipTo}",
                    streamInfo.StreamId, skipTo.ToShortString());

                await EnqueueAudioSource(playback, streamInfo, audioSource, playAt, skipTo, cancellationToken)
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
        LiveAudioStreamInfo streamInfo,
        IAsyncEnumerable<AudioFrame> audioFrames,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var format = streamInfo.Format ?? AudioSource.DefaultFormat;
        var frameStream = audioFrames
            .SkipWhile(f => f.Offset < skipTo)
            .Select(f => new AudioFrame {
                Data = f.Data,
                Offset = f.Offset - skipTo,
                Duration = f.Duration,
            });

        return new AudioSource(
            streamInfo.BeginsAt + skipTo,
            format,
            frameStream,
            TimeSpan.Zero,
            Log,
            cancellationToken);
    }

    private async Task EnqueueAudioSource(
        Playback playback,
        LiveAudioStreamInfo streamInfo,
        AudioSource audioSource,
        Moment playAt,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        // Get chat and author info for track metadata
        var chat = await Hub.Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        var author = await Hub.Authors.Get(Session, ChatId, streamInfo.AuthorId, cancellationToken).ConfigureAwait(false);

        if (chat == null)
            return;

        // SourceRecordedAt routes through to JS as recordedAtMs and feeds the
        // audio-side presentation-lag callback. Use SourceBeginsAt (raw client
        // claim) so the audio side's lag is identical to video's lag; fall back
        // to BeginsAt on legacy/replay streams that don't carry SourceBeginsAt.
        var sourceRecordedAt = (streamInfo.SourceBeginsAt != default
            ? streamInfo.SourceBeginsAt
            : streamInfo.BeginsAt) + skipTo;
        var targetBufferSize = await Hub.ChatAudioUI
            .GetPlaybackTargetBufferSize(ChatId, cancellationToken)
            .ConfigureAwait(false);
        var trackInfo = new ChatAudioTrackInfo(ChatId, null, chat, author) {
            RecordedAt = streamInfo.BeginsAt + skipTo,
            SourceRecordedAt = sourceRecordedAt,
            TargetBufferSize = targetBufferSize,
        };

        playback.Play(trackInfo, audioSource, playAt, cancellationToken);
    }

    // Nested types

    private sealed class PlayState(Moment startAt, TimeSpan lastSleepDuration)
    {
        public Lock Lock { get; } = new();
        public Moment StartAt { get; private set; } = startAt;
        public TimeSpan LastSleepDuration { get; private set; } = lastSleepDuration;
        public Moment LastNotifyTunePlayedAt { get; set; } = startAt;

        public void Reset(Moment startAt, TimeSpan lastSleepDuration)
        {
            StartAt = LastNotifyTunePlayedAt = startAt;
            LastSleepDuration = lastSleepDuration;
        }
    }
}
