using ActualChat.Kvas;
using Stl.Locking;

namespace ActualChat.Chat.UI.Blazor.Services;

public class UnreadMessages : IDisposable
{
    private const int MaxCount = 1000;

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

    public async Task<bool> HasMentions(CancellationToken cancellationToken)
    {
        var lastReadEntryId = await GetLastReadEntryId(cancellationToken).ConfigureAwait(false);
        if (lastReadEntryId == null)
            return false; // Never opened this chat, so no unread mentions

        var lastMention = await Mentions
            .GetLast(Session, ChatId, cancellationToken)
            .ConfigureAwait(false);

        return lastMention?.EntryId > lastReadEntryId;
    }

    public async ValueTask<MaybeEstimate<int>> GetCount(CancellationToken cancellationToken)
    {
        var lastReadEntryId = await GetLastReadEntryId(cancellationToken);
        if (lastReadEntryId == null)
            return 0; // Never opened this chat, so no unread messages

        var idRange = await Chats
            .GetIdRange(Session, ChatId, ChatEntryType.Text, cancellationToken)
            .ConfigureAwait(false);
        var exactCount = (int)(idRange.End - lastReadEntryId.Value - 1).Clamp(0, MaxCount);
        if (exactCount >= 15)
            return (exactCount, true);
        return exactCount;
    }

    // TODO: in fact it should not nullable
    private async Task<long?> GetLastReadEntryId(CancellationToken cancellationToken)
    {
        using (var _ = await _asyncLock.Lock(cancellationToken).ConfigureAwait(false))
            _lastReadEntryState ??= await ChatUI.LeaseLastReadEntryState(ChatId, cancellationToken).ConfigureAwait(false);

        return await _lastReadEntryState.Use(cancellationToken).ConfigureAwait(false);
    }

    private static int RoundTo(long value, long @base)
        => (int)(value / @base * @base).Clamp(0, MaxCount);
}
