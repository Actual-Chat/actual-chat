namespace ActualChat.Chat;

public class Conversations(IServiceProvider services) : IConversations
{
    private IConversationsBackend Backend { get; } = services.GetRequiredService<IConversationsBackend>();

    private IChats Chats { get; } = services.GetRequiredService<IChats>();

    // [Computed]
    public virtual async Task<Conversation[]> GetTile(Session session, ChatId chatId, Range<long> idTileRange, CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanRead())
            return [];

        return await Backend.GetTile(chatId, idTileRange, cancellationToken).ConfigureAwait(false);
    }
}
