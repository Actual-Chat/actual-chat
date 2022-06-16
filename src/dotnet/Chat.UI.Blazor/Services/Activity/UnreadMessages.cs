using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class UnreadMessages
{
    private readonly Session _session;
    private readonly IChats _chats;
    private readonly Symbol _chatId;
    private readonly IPersistentState<long> _lastReadEntryId;

    public UnreadMessages(Session session, Symbol chatId, IPersistentState<long> lastReadEntryId, IChats chats)
    {
        _session = session;
        _chats = chats;
        _chatId = chatId;
        _lastReadEntryId = lastReadEntryId;
    }

    public async Task<int?> GetCount(CancellationToken cancellationToken)
    {
        var lastReadEntryId = await _lastReadEntryId.Use(cancellationToken).ConfigureAwait(false);
        if (lastReadEntryId == 0)
            return null;

        var idRange = await _chats.GetIdRange(_session, _chatId, ChatEntryType.Text, cancellationToken)
            .ConfigureAwait(false);

        return Math.Max((int)(idRange.ToInclusive().End - lastReadEntryId), 0);
    }
}
