namespace ActualChat.Chat;

public class Conversations(IServiceProvider services) : IConversations
{
    private static readonly TileStack<long> IdTileStack = Constants.Chat.ServerIdTileStack;

    private IConversationsBackend Backend { get; } = services.GetRequiredService<IConversationsBackend>();

    private IChats Chats { get; } = services.GetRequiredService<IChats>();

    // [Computed]
    public virtual async Task<Conversation?> Get(Session session, ConversationId conversationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversationId);

        var rules = await Chats.GetRules(session, conversationId.ChatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanRead())
            return null;

        return await Backend.Get(conversationId, cancellationToken).ConfigureAwait(false);
    }

    // [Computed]
    public virtual async Task<Conversation[]> GetTile(Session session, ChatId chatId, Range<long> idTileRange, CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanRead())
            return [];

        return await Backend.GetTile(chatId, idTileRange, cancellationToken).ConfigureAwait(false);
    }
}
