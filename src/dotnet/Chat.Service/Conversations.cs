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
        await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false); // Make sure we can read the chat

        var idTiles = IdTileStack.LastLayer.GetCoveringTiles(idTileRange);
        var conversationTiles = await idTiles
            .Select(idTile => Backend.GetRangeMeta(chatId, idTile.Range.Start, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);
        var conversations = await conversationTiles
            .SelectMany(ct => ct.ConversationIds)
            .Distinct()
            .Select(cId => Backend.Get(cId, cancellationToken))
            .Collect(cancellationToken)
            .ConfigureAwait(false);

        return conversations
            .Where(c => c != null && !c.EntryRange.IntersectWith(idTileRange).IsEmpty)
            .OrderBy(c => c!.EntryRange.Start)
            .ToArray()!;
    }
}
