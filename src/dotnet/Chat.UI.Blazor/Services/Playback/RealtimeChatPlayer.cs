namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class RealtimeChatPlayer : ChatPlayer
{
    /// <summary> Min. delay is ~ 2.5*Ping, so we can skip something </summary>
    private static readonly TimeSpan SkipTo = TimeSpan.Zero;
    private static readonly TimeSpan MaxClientToServerTimeOffset = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxEntryBeginsAtDisorder = TimeSpan.FromSeconds(5);

    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.AudioPlayback;

    public RealtimeChatPlayer(Session session, ChatId chatId, IServiceProvider services)
        : base(session, chatId, services)
        => PlayerKind = ChatPlayerKind.Realtime;

    // ReSharper disable once RedundantAssignment
    protected override async Task Play(
        ChatEntryPlayer entryPlayer, Moment startAt, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(Session, ChatId, cancellationToken).ConfigureAwait(false);
        if (chat == null || !chat.Rules.CanRead())
            return;

        Operation = $"listening \"{chat.Title}\"";
        // We always override startAt here
        startAt = Clocks.SystemClock.Now - MaxClientToServerTimeOffset;
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
            if (!entry.IsStreaming)
                // Non-streaming entry:
                // - We were asleep & missed a bunch of entries
                // - Or audioEntryReader is still enumerating "early" entries
                //   @ (startAt - ChatConstants.MaxEntryDuration)
                continue;

            if (!Constants.DebugMode.AudioPlaybackPlayMyOwnAudio) {
                var author = await Authors.GetOwn(Session, ChatId, cancellationToken)
                    .ConfigureAwait(false);
                if (author != null && entry.AuthorId == author.Id)
                    continue;
            }

            DebugLog?.LogDebug("[RealtimeChatPlayer] Player.EnqueueEntry: {ChatId}, {EntryId}", ChatId, entry.Id);
            startAt = Moment.Max(startAt, entry.BeginsAt - MaxEntryBeginsAtDisorder);
            if (!await CanContinuePlayback(cancellationToken).ConfigureAwait(false))
                return;

            entryPlayer.EnqueueEntry(entry, SkipTo);
        }
    }
}
