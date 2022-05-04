using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class UnreadMessages
{
    private readonly IChats _chats;
    private readonly IChatReadPositions _readPositions;

    public UnreadMessages(IChats chats, IChatReadPositions readPositions)
    {
        _chats = chats;
        _readPositions = readPositions;
    }

    public async Task<int?> GetUnreadMessageCount(Session session, string chatId, CancellationToken cancellationToken)
    {
        var readPosition = await _readPositions.GetReadPosition(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!readPosition.HasValue)
            return null;

        var idRange = await _chats.GetIdRange(session, chatId, ChatEntryType.Text, cancellationToken)
            .ConfigureAwait(false);

        return Math.Max((int)(idRange.ToInclusive().End - readPosition.Value), 0);
    }
}
