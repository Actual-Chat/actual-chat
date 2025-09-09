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

    public virtual async Task<Conversation?> Get(Session session, ConversationId conversationId, CancellationToken cancellationToken)
    {
        var rules = await Chats.GetRules(session, conversationId.ChatId, cancellationToken).ConfigureAwait(false);
        if (!rules.CanRead())
            return null;

        return await Backend.Get(conversationId, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnReSummarize(Conversations_Summarize command, CancellationToken cancellationToken)
    {
        var (session, conversationId) = command;
        var chatId = conversationId.ChatId;
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(ChatPermissions.Owner);

        var conversation = await Backend.Get(conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation == null)
            throw new NotFoundException<Conversation>();

        var entryIdRange = conversation.EntryRange;
        var reSummarizeCommand = new ConversationBackend_Summarize(chatId, [entryIdRange]);
        await Commander.Call(reSummarizeCommand, cancellationToken).ConfigureAwait(false);
    }
}
