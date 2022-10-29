namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class RealtimeChatPlayer : ChatPlayer
{
    /// <summary> Min. delay is ~ 2.5*Ping, so we can skip something </summary>
    private static readonly TimeSpan StreamingSkipTo = TimeSpan.Zero;

    public RealtimeChatPlayer(Session session, Symbol chatId, IServiceProvider services)
        : base(session, chatId, services)
        => PlayerKind = ChatPlayerKind.Realtime;

    // ReSharper disable once RedundantAssignment
    protected override async Task Play(
        ChatEntryPlayer entryPlayer, Moment startAt, CancellationToken cancellationToken)
    {
        startAt = Clocks.SystemClock.Now; // We always override startAt here
        var audioEntryReader = Chats.NewEntryReader(Session, ChatId, ChatEntryType.Audio);
        var idRange = await Chats.GetIdRange(Session, ChatId, ChatEntryType.Audio, cancellationToken)
            .ConfigureAwait(false);
        var startEntry = await audioEntryReader
            .FindByMinBeginsAt(startAt - Constants.Chat.MaxEntryDuration, idRange, cancellationToken)
            .ConfigureAwait(false);
        var startId = startEntry?.Id ?? idRange.End;

        var entries = audioEntryReader.Observe(startId, cancellationToken);
        await foreach (var entry in entries.ConfigureAwait(false)) {
            if (entry.EndsAt < startAt)
                // We're starting @ (startAt - ChatConstants.MaxEntryDuration),
                // so we need to skip a few entries.
                // Note that streaming entries have EndsAt == null, so we don't skip them.
                continue;

            if (!Constants.DebugMode.AudioPlaybackPlayMyOwnAudio) {
                var chatAuthor = await ChatAuthors.GetOwn(Session, ChatId, cancellationToken)
                    .ConfigureAwait(false);
                if (chatAuthor != null && entry.AuthorId == chatAuthor.Id)
                    continue;
            }

            var skipToOffset = entry.IsStreaming ? StreamingSkipTo : TimeSpan.Zero;
            var entryBeginsAt = Moment.Max(entry.BeginsAt + skipToOffset, startAt);
            var skipTo = entryBeginsAt - entry.BeginsAt;

            entryPlayer.EnqueueEntry(entry, skipTo);
        }
    }
}
