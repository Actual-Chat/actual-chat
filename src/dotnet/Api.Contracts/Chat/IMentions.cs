namespace ActualChat.Chat;

public interface IMentions : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<Mention?> GetLastOwn(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);
}
