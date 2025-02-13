namespace ActualChat.Chat;

public class ConversationsBackend : IConversationsBackend
{
    // [ComputeMethod]
    public virtual Task<Conversation?> Get(ConversationId conversationId, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    // [ComputeMethod]
    public virtual Task<ApiArray<Conversation>> List(ChatId chatId, Range<long> idTileRange, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    // Commands

    // [CommandHandler]
    public virtual Task<Conversation> OnUpsert(ConversationBackend_Upsert command, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    // [CommandHandler]
    public virtual Task<Conversation> OnSummarize(ConversationBackend_Summarize command, CancellationToken cancellationToken)
        => throw new NotImplementedException();

    // [CommandHandler]
    public Task<Conversation> OnAppendReply(ConversationBackend_AppendReply command, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
