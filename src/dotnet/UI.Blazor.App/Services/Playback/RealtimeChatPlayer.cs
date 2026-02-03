using ActualChat.Audio;
using ActualChat.Rtc;
using ActualChat.UI.Blazor.App.Services.Rtc;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class RealtimeChatPlayer : ChatPlayer
{
    private ChatAudioUI ChatAudioUI { get; }

    public RealtimeChatPlayer(AppUIHub hub, ChatId chatId)
        : base(hub, chatId)
    {
        ChatAudioUI = Hub.ChatAudioUI;
        PlayerKind = ChatPlayerKind.Realtime;
    }

    protected override async Task Play(
        ChatEntryPlayer entryPlayer, Moment minPlayAt, CancellationToken cancellationToken)
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

        // Connect to RTC Hub with automatic reconnection
        var settings = RtcStreamingSettings.Default;
        var processor = new RtcStreamProcessor(
            Hub.Services, Session, ChatId, settings, cancellationToken.CreateLinkedTokenSource());
        await using var _ = processor.ConfigureAwait(false);

        processor.StreamStarted +=
            args => OnStreamStarted(args, entryPlayer, state, serverClock, cancellationToken);
        await processor.Run().ConfigureAwait(false);
    }

    private void OnStreamStarted(
        RtcStreamStartedArgs args,
        ChatEntryPlayer entryPlayer,
        PlayState state,
        MomentClock serverClock,
        CancellationToken cancellationToken)
    {
        _ = BackgroundTask.Run(async () => {
            try {
                // Skip own audio unless in debug mode
                if (!Constants.DebugMode.ChatPlayersPlayMyOwnAudio && args.AuthorId != null) {
                    var author = await Authors.GetOwn(Session, ChatId, cancellationToken)
                        .ConfigureAwait(false);
                    if (author != null && args.AuthorId == author.Id)
                        return;
                }

                if (!await CanContinuePlayback(cancellationToken).ConfigureAwait(false)) {
                    await ChatAudioUI.SetListeningState(ChatId, false).ConfigureAwait(false);
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

                var playAt = Moment.Max(minPlayAt, args.BeginsAt);
                if (playAt >= args.BeginsAt + Constants.Chat.MaxEntryDuration)
                    return;

                // Play notification sound for new message after delay
                if (args.BeginsAt - state.LastStreamBeginsAt > Hub.AudioSettings.IdleListeningNewMessageTrigger)
                    await Hub.TuneUI.PlayAndWait(Tune.NotifyOnNewAudioMessageAfterDelay).ConfigureAwait(false);

                state.LastStreamBeginsAt = args.BeginsAt;

                // Create AudioSource from the stream frames
                var skipTo = (playAt - args.BeginsAt).Positive();
                var audioSource = CreateAudioSource(args, skipTo, cancellationToken);

                // Enqueue for playback
                DebugLog?.LogDebug("Play: enqueuing stream #{StreamIndex} @ {SkipTo}",
                    args.StreamIndex, skipTo.ToShortString());

                await EnqueueAudioSource(entryPlayer, args, audioSource, playAt, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Expected
            }
            catch (Exception e) {
                Log.LogWarning(e, "Error processing stream #{StreamIndex}", args.StreamIndex);
            }
        }, CancellationToken.None);
    }

    private AudioSource CreateAudioSource(
        RtcStreamStartedArgs args,
        TimeSpan skipTo,
        CancellationToken cancellationToken)
    {
        var format = args.Format ?? AudioSource.DefaultFormat;
        var frameStream = args.AudioFrames
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
            args.BeginsAt,
            format,
            frameStream,
            TimeSpan.Zero,
            Log,
            cancellationToken);
    }

    private async Task EnqueueAudioSource(
        ChatEntryPlayer entryPlayer,
        RtcStreamStartedArgs args,
        AudioSource audioSource,
        Moment playAt,
        CancellationToken cancellationToken)
    {
        // Get chat and author info for track metadata
        var chat = await Hub.Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        Author? author = null;
        if (args.AuthorId != null)
            author = await Hub.Authors.Get(Session, ChatId, args.AuthorId, cancellationToken).ConfigureAwait(false);

        if (chat == null)
            return;

        // Create track info for RTC stream
        var trackInfo = new ChatAudioTrackInfo(ChatId, args.EntryId, chat, author) {
            RecordedAt = args.BeginsAt,
            ClientSideRecordedAt = args.BeginsAt,
        };

        entryPlayer.Playback.Play(trackInfo, audioSource, playAt, cancellationToken);
    }

    // Nested types

    private sealed class PlayState
    {
        public TimeSpan SyncedSleepDuration { get; set; }
        public Moment MinPlayAt { get; set; }
        public Moment LastStreamBeginsAt { get; set; }
    }
}
