using MemoryPack;

namespace ActualChat.Chat;

public interface IConversations : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<Conversation[]> GetTile(
        Session session,
        ChatId chatId,
        Range<long> idTileRange,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<Conversation?> Get(Session session, ConversationId conversationId, CancellationToken cancellationToken);

    [CommandHandler]
    Task OnReSummarize(Conversations_Summarize command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Conversations_Summarize(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ConversationId ConversationId
) : ISessionCommand<Unit>, IApiCommand;
