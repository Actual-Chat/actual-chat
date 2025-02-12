namespace ActualChat.Chat;

public class ConversationsBackend : IConversationsBackend
{
    // [CommandHandler]
    public virtual Task<Conversation> OnUpsert(ConversationBackend_Upsert command, CancellationToken cancellationToken)
        => throw new NotImplementedException();
}
