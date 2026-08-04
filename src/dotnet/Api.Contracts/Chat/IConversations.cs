namespace ActualChat.Chat;

/// <summary>
/// Service for managing conversation segments and their summaries.
/// </summary>
public interface IConversations : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<Conversation[]> GetTile(
        Session session,
        ChatId chatId,
        Range<long> lidTileRange,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Conversation?> Get(Session session, ConversationId conversationId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnReSummarize(Conversations_Summarize command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Conversations_Summarize(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ConversationId ConversationId
) : ISessionCommand<Unit>, IApiCommand;
