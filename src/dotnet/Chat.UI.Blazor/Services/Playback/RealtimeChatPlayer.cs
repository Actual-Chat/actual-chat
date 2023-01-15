namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class RealtimeChatPlayer : ChatPlayer
{
    /// <summary> Min. delay is ~ 2.5*Ping, so we can skip something </summary>
    private static readonly TimeSpan StreamingSkipTo = TimeSpan.Zero;

    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.AudioPlayback;

    public RealtimeChatPlayer(Session session, ChatId chatId, IServiceProvider services)
        : base(session, chatId, services)
        => PlayerKind = ChatPlayerKind.Realtime;

    // ReSharper disable once RedundantAssignment
    protected override async Task Play(
        ChatEntryPlayer entryPlayer, Moment startAt, CancellationToken cancellationToken)
    {
        startAt = Clocks.SystemClock.Now; // We always override startAt here
        DebugLog?.LogDebug("[RealtimeChatPlayer] Play: {ChatId}, {StartedAt}", ChatId, startAt);
        var audioEntryReader = Chats.NewEntryReader(Session, ChatId, ChatEntryKind.Audio);
        var idRange = await Chats.GetIdRange(Session, ChatId, ChatEntryKind.Audio, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await audioEntryReader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        var startId = startEntry?.LocalId ?? idRange.End;

        var entries = audioEntryReader.Observe(startId, cancellationToken);
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (entry.EndsAt < startAt)
                // We're starting @ (startAt - ChatConstants.MaxEntryDuration),
                // so we need to skip a few entries.
                // Note that streaming entries have EndsAt == null, so we don't skip them.
                continue;

            if (!Constants.DebugMode.AudioPlaybackPlayMyOwnAudio) {
                var author = await Authors.GetOwn(Session, ChatId, cancellationToken)
                    .ConfigureAwait(false);
                if (author != null && entry.AuthorId == author.Id)
                    continue;
            }

            var skipToOffset = entry.IsStreaming ? StreamingSkipTo : TimeSpan.Zero;
            var entryBeginsAt = Moment.Max(entry.BeginsAt + skipToOffset, startAt);
            var skipTo = entryBeginsAt - entry.BeginsAt;

            DebugLog?.LogDebug("[RealtimeChatPlayer] Player.EnqueueEntry: {ChatId}, {EntryId}", ChatId, entry.Id);
            entryPlayer.EnqueueEntry(entry, skipTo);
        }
    }
}
