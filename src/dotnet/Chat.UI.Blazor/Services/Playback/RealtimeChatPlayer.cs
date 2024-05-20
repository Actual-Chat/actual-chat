using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class RealtimeChatPlayer : ChatPlayer
{

    private ChatAudioUI ChatAudioUI { get; }

    public RealtimeChatPlayer(ChatUIHub hub, ChatId chatId)
        : base(hub, chatId)
    {
        ChatAudioUI = Hub.ChatAudioUI;
        PlayerKind = ChatPlayerKind.Realtime;
    }

    // ReSharper disable once RedundantAssignment
    protected override async Task Play(
        ChatEntryPlayer entryPlayer, Moment minPlayAt, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        if (chat == null || !chat.Rules.CanRead())
            return;

        var serverClock = Clocks.ServerClock;
        await serverClock.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);

        Operation = $"listening in \"{chat.Title}\"";
        // We always override startAt here
        DebugLog?.LogDebug("Play: {ChatId}, {StartedAt}", ChatId, minPlayAt);

        var audioEntryReader = Hub.NewEntryReader(ChatId, ChatEntryKind.Audio);
        var idRange = await Chats.GetIdRange(Session, ChatId, ChatEntryKind.Audio, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await audioEntryReader
            .FindByMinBeginsAt(minPlayAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        var startId = startEntry?.LocalId ?? idRange.End;
        var syncedSleepDuration = SleepDuration.Value;
        var startedAt = serverClock.Now;
        var lastEntryBeginsAt = startedAt;
        minPlayAt = startedAt;

        var entries = audioEntryReader.Observe(startId, cancellationToken);
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (!entry.IsStreaming && entry.BeginsAt <= startedAt)
                // Non-streaming entry:
                // - We were asleep & missed a bunch of entries
                // - Or audioEntryReader is still enumerating "early" entries
                //   @ (startedAt - ChatConstants.MaxEntryDuration)
                continue;

            if (!Constants.DebugMode.AudioPlaybackPlayMyOwnAudio) {
                var author = await Authors.GetOwn(Session, ChatId, cancellationToken)
                    .ConfigureAwait(false);
                if (author != null && entry.AuthorId == author.Id)
                    continue;
            }

            if (!await CanContinuePlayback(cancellationToken).ConfigureAwait(false)) {
                await ChatAudioUI.SetListeningState(ChatId, false).ConfigureAwait(false);
                return;
            }

            // We don't move minPlayAt forward here only when sleep is detected,
            // coz if they simply track ServerClock.Now & ServerClock is somehow
            // ahead of actual server clock, it's going to skip the beginning of
            // every message rather than just of the initial one / post-sleep ones.
            var sleepDuration = SleepDuration.Value;
            if (sleepDuration - syncedSleepDuration > Constants.Audio.MaxRealtimeStreamDrift) {
                minPlayAt = serverClock.Now - Constants.Audio.MaxRealtimeStreamDrift; // Re-sync minPlayAt
                syncedSleepDuration = sleepDuration;
            }
            var playAt = Moment.Max(minPlayAt, entry.BeginsAt);
            if (entry.EndsAt.HasValue && playAt >= entry.EndsAt)
                continue; // already completed streaming entry

            if (playAt >= entry.BeginsAt + Constants.Chat.MaxEntryDuration) // no EndsAt for streaming entries
                continue;

            if (entry.BeginsAt - lastEntryBeginsAt > Hub.AudioSettings.IdleListeningNewMessageTrigger)
                await Hub.TuneUI.PlayAndWait(Tune.NotifyOnNewAudioMessageAfterDelay).ConfigureAwait(false);

            var skipTo = (playAt - entry.BeginsAt).Positive();
            DebugLog?.LogDebug("Play: enqueuing #{EntryId} @ {SkipTo}", entry.Id, skipTo.ToShortString());
            entryPlayer.EnqueueEntry(entry, skipTo);
            lastEntryBeginsAt = entry.BeginsAt;
        }
    }
}
