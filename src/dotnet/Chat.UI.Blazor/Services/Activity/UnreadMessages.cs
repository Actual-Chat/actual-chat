using ActualChat.Kvas;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

public class UnreadMessages : IDisposable
{
    public const int MaxCount = 1000;

    private Session Session { get; }
    private IChats Chats { get; }
    private Symbol ChatId { get; }
    private ChatUI ChatUI { get; }
    private IMentions Mentions { get; }
    private readonly AsyncLock _asyncLock = new(ReentryMode.CheckedFail);
    private SyncedStateLease<long?>? _lastReadEntryState;

    public UnreadMessages(Session session, Symbol chatId, ChatUI chatUI, IChats chats, IMentions mentions)
    {
        Session = session;
        Chats = chats;
        ChatId = chatId;
        ChatUI = chatUI;
        Mentions = mentions;
    }

    public void Dispose()
        => _lastReadEntryState?.Dispose();

    public async ValueTask<bool> HasMentions(CancellationToken cancellationToken)
    {
        // Let's start this in parallel
        var lastReadEntryIdTask = GetLastReadEntryId(cancellationToken);
        var getMentionsTask = Mentions.GetLast(Session, ChatId, cancellationToken);

        var lastReadEntryId = await lastReadEntryIdTask.ConfigureAwait(false);
        if (lastReadEntryId == null)
            return false; // Never opened this chat, so no unread mentions

        var lastMention = await getMentionsTask.ConfigureAwait(false);
        return lastMention?.EntryId > lastReadEntryId;
    }

    public async ValueTask<MaybeTrimmed<int>> GetCount(CancellationToken cancellationToken)
    {
        // Let's start this in parallel
        var lastReadEntryIdTask = GetLastReadEntryId(cancellationToken);
        var getSummaryTask = Chats.GetSummary(Session, ChatId, cancellationToken);

        var lastReadEntryId = await lastReadEntryIdTask.ConfigureAwait(false);
        if (lastReadEntryId == null)
            return 0; // Never opened this chat, so no unread messages

        var summary = await getSummaryTask.ConfigureAwait(false);
        if (summary == null)
            return 0;

        var lastId = summary.TextEntryIdRange.End - 1;
        var count = (int)(lastId - lastReadEntryId.Value).Clamp(0, MaxCount);
        return (count, count == MaxCount);
    }

    // TODO: in fact it should not nullable
    private async ValueTask<long?> GetLastReadEntryId(CancellationToken cancellationToken)
    {
        using (var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false))
            _lastReadEntryState ??= await ChatUI.LeaseLastReadEntryState(ChatId, cancellationToken).ConfigureAwait(false);

        return await _lastReadEntryState.Use(cancellationToken).ConfigureAwait(false);
    }

    private static int RoundTo(long value, long @base)
        => (int)(value / @base * @base).Clamp(0, MaxCount);
}
