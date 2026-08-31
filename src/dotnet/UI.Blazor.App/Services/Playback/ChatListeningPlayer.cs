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
        // No focus release: IsPaused makes ShouldHoldListeningFocus skip this chat, so the burst
        // manager hands the focus back after its linger - streams still arriving or not.
        => _ = Playback.Pause(CancellationToken.None);

    public override async Task Resume()
    {
        var listeningChatIds = await ChatAudioUI.GetListeningChatIds().ConfigureAwait(false);
        if (!listeningChatIds.Contains(ChatId)) {
            Log.LogInformation("Can't resume listening playback. ChatId '{ChatId}' not in listening set", ChatId);
            return;
        }

        // No direct focus acquire: the resumed playback flips ShouldHoldListeningFocus, and the
        // burst manager takes the focus from there - a direct grab here leaked it when nothing
        // was streaming, since only a completed burst cycle ever releases it.
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
        var state = new PlayState(startAt);

        var streamProcessor = new ListeningStreamProcessor(
            Hub.Services, Session, ChatId,
            ChatAudioUI.GetListeningCatchUp(ChatId),
            cancellationToken.CreateLinkedTokenSource());
        await using var _ = streamProcessor.ConfigureAwait(false);

        streamProcessor.StreamStarted +=
            (info, _, frames) => OnStreamStarted(playback, state, info, frames, cancellationToken);
        StartSleepWatcher(streamProcessor, cancellationToken);
        await streamProcessor.Run().ConfigureAwait(false);
    }

    // Private methods

    private void StartSleepWatcher(
        ListeningStreamProcessor streamProcessor,
        CancellationToken cancellationToken)
        => _ = BackgroundTask.Run(
            () => ResubscribeOnSleep(streamProcessor, cancellationToken),
            Log,
            $"Sleep watcher failed for #{ChatId}",
            cancellationToken);

    private async Task ResubscribeOnSleep(
        ListeningStreamProcessor streamProcessor,
        CancellationToken cancellationToken)
    {
        // Changes fires once on subscription with the current value; that one isn't a wake-up.
        var isFirst = true;
        await foreach (var _ in SleepDuration.Computed.Changes(cancellationToken).ConfigureAwait(false)) {
            if (isFirst) {
                isFirst = false;
                continue;
            }

            Log.LogInformation("Re-subscribing to #{ChatId} after sleep", ChatId);
            streamProcessor.Break();
        }
    }

    private void OnStreamStarted(
        Playback playback,
        PlayState state,
        LiveAudioStreamInfo streamInfo,
        IAsyncEnumerable<AudioFrame> audioFrames,
        CancellationToken cancellationToken)
    {
        _ = BackgroundTask.Run(async () => {
            try {
                if (!Constants.DebugMode.ListenOwnAudio) {
                    var author = await Authors.GetOwn(Session, ChatId, cancellationToken).ConfigureAwait(false);
                    if (author is { } own && streamInfo.AuthorId == own.Id)
                        return;
                }

                if (!await CanContinuePlayback(cancellationToken).ConfigureAwait(false)) {
                    await ChatAudioUI.SetListeningState(ChatId, false).ConfigureAwait(false);
                    return;
                }

                // The cue prepares the user for audio after real silence, so a chat with a live
                // session already running (incoming or own outgoing, audio or video) never plays it.
                var liveSessionState = await Hub.LiveSessionUI.GetState(ChatId, cancellationToken)
                    .ConfigureAwait(false);
                var isSessionActive = liveSessionState is { SessionStartedAt: not null }
                    || await Hub.ChatVideoUI.IsAnyoneVideoStreaming(ChatId, cancellationToken).ConfigureAwait(false);

                lock (state.Lock) {
                    // The stamp tracks the last INCOMING stream, not the last cue - measuring
                    // against the cue re-fired it mid-conversation every trigger interval.
                    var idleFor = streamInfo.BeginsAt - state.LastIncomingAudioAt;
                    if (!isSessionActive && idleFor > Hub.AudioSettings.IdleListeningNewMessageTrigger)
                        _ = Hub.TuneUI.Play(Tune.NotifyOnNewAudioMessageAfterDelay);
                    state.LastIncomingAudioAt = streamInfo.BeginsAt;
                }

                // Nothing is trimmed here. The server serves this stream from its live edge when
                // the muxer first sees it and whole afterwards, so anything that arrives is meant
                // to be heard - and the receiver has no clock it could trim by that isn't the
                // difference of two independent estimates of server time.
                var audioSource = CreateAudioSource(streamInfo, audioFrames, cancellationToken);
                DebugLog?.LogDebug("Play: enqueuing stream #{StreamId}", streamInfo.StreamId);

                await EnqueueAudioSource(playback, streamInfo, audioSource, cancellationToken)
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
        CancellationToken cancellationToken)
        => new(
            streamInfo.BeginsAt,
            streamInfo.Format ?? AudioSource.DefaultFormat,
            audioFrames,
            TimeSpan.Zero,
            Log,
            cancellationToken);

    private async Task EnqueueAudioSource(
        Playback playback,
        LiveAudioStreamInfo streamInfo,
        AudioSource audioSource,
        CancellationToken cancellationToken)
    {
        var chat = await Hub.Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        var author = await Hub.Authors
            .Get(Session, ChatId, streamInfo.AuthorId, cancellationToken)
            .ConfigureAwait(false);

        if (chat is null)
            return;

        // SourceRecordedAt routes through to JS as recordedAtMs and feeds the
        // audio-side presentation-lag callback. BeginsAt, not SourceBeginsAt:
        // every producer sets them equal except the live backend's gross-skew
        // guard, which rebases BeginsAt onto server time exactly when the raw
        // claim is broken — mirrors the video side's StartedAt anchor.
        var targetBufferSize = await Hub.ChatAudioUI
            .GetPlaybackTargetBufferSize(ChatId, cancellationToken)
            .ConfigureAwait(false);
        var trackInfo = new ChatAudioTrackInfo(ChatId, null, chat, author) {
            RecordedAt = streamInfo.BeginsAt,
            SourceRecordedAt = streamInfo.BeginsAt,
            TargetBufferSize = targetBufferSize,
            StreamId = streamInfo.StreamId,
        };

        playback.Play(trackInfo, audioSource, cancellationToken);
    }

    // Nested types

    private sealed class PlayState(Moment startAt)
    {
        public Lock Lock { get; } = new();
        public Moment LastIncomingAudioAt { get; set; } = startAt;
    }
}
