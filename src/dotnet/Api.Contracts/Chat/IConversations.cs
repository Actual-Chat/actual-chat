namespace ActualChat.Chat;

public interface IConversations : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<Conversation?> Get(Session session, ConversationId conversationId, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 300)]
    Task<ApiArray<Conversation>> GetTile(
        Session session,
        ChatId chatId,
        Range<long> idTileRange,
        CancellationToken cancellationToken);
}
