namespace ActualChat.Chat;

public class Conversations(IServiceProvider services) : IConversations
{
    private IConversationsBackend Backend { get; } = services.GetRequiredService<IConversationsBackend>();

    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private ICommander Commander { get; } = services.GetRequiredService<ICommander>();

    // [Computed]
    public virtual async Task<Conversation[]> GetTile(Session session, ChatId chatId, Range<long> idTileRange, CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, chatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanRead())
            return [];

        return await Backend.GetTile(chatId, idTileRange, cancellationToken).ConfigureAwait(false);
    }

    public virtual Task<Conversation?> Get(ConversationId conversationId, CancellationToken cancellationToken)
        => Backend.Get(conversationId, cancellationToken);

    public virtual async Task<Conversation?> ReSummarize(ConversationId conversationId, CancellationToken cancellationToken)
    {
        var conversation = await Backend.Get(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation == null)
            return null;

        var chatId = conversationId.ChatId;
        var entryIdRange = conversation.EntryRange;
        var command = new ConversationBackend_Summarize(chatId, [entryIdRange]);
        return await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }
}
