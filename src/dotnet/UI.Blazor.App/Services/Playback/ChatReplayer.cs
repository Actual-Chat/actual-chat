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
        var offset = replayState is { ChatId: var rsChat } && rsChat == ChatId
            ? replayState.Offset
            : TimeSpan.Zero;

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

        processor.StreamStarted += (info, frames) =>
            OnStreamStarted(entryPlayer, info, frames, cancellationToken);
        await processor.Run().ConfigureAwait(false);
    }

    private void OnStreamStarted(
        ChatEntryPlayer entryPlayer,
        LiveStreamInfo streamInfo,
        IAsyncEnumerable<byte[]> audioFrames,
        CancellationToken cancellationToken)
    {
        _ = BackgroundTask.Run(async () => {
            try {
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
                entryPlayer.Playback.Play(trackInfo, audioSource, playAt, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Expected
            }
            catch (Exception e) {
                Log.LogWarning(e, "Error processing replay stream {StreamId}", streamInfo.StreamId);
            }
        }, CancellationToken.None);
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
