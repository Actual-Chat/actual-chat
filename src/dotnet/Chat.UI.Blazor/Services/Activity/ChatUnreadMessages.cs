using ActualChat.Kvas;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatUnreadMessages : IDisposable
{
    public const int MaxCount = 1000;

    private readonly AsyncLock _asyncLock = new(ReentryMode.CheckedFail);
    private SyncedStateLease<long?>? _lastReadEntryState;

    private Session Session { get; }
    private Symbol ChatId { get; }
    private IChats Chats { get; }
    private IMentions Mentions { get; }
    private ChatUI ChatUI { get; }

    public ChatUnreadMessages(Session session, Symbol chatId, IChats chats, IMentions mentions, ChatUI chatUI)
    {
        Session = session;
        ChatId = chatId;
        Chats = chats;
        Mentions = mentions;
        ChatUI = chatUI;
    }

    public void Dispose()
        => _lastReadEntryState?.Dispose();

    public async Task<bool> HasMentions(CancellationToken cancellationToken)
    {
        // Let's start this in parallel
        var lastReadEntryIdTask = GetLastReadEntryId(cancellationToken);
        var getMentionsTask = Mentions.GetLastOwn(Session, ChatId, cancellationToken);

        var lastReadEntryId = await lastReadEntryIdTask.ConfigureAwait(false);
        if (lastReadEntryId == null)
            return false; // Never opened this chat, so no unread mentions

        var lastMention = await getMentionsTask.ConfigureAwait(false);
        return lastMention?.EntryId > lastReadEntryId;
    }

    public async Task<MaybeTrimmed<int>> GetCount(CancellationToken cancellationToken)
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
    private async Task<long?> GetLastReadEntryId(CancellationToken cancellationToken)
    {
        using (var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false))
            _lastReadEntryState ??= await ChatUI.LeaseLastReadEntryState(ChatId, cancellationToken).ConfigureAwait(false);

        return await _lastReadEntryState.Use(cancellationToken).ConfigureAwait(false);
    }
}
