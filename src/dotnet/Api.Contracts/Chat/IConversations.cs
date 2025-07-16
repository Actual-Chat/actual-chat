namespace ActualChat.Chat;

public interface IConversations : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<Conversation[]> GetTile(
        Session session,
        ChatId chatId,
        Range<long> idTileRange,
        CancellationToken cancellationToken);
}
