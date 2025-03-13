namespace ActualChat.Chat;

public interface ITranslations : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache)]
    Task<Translation?> Get(Session session, TranslationId id, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ChatEntryLanguage?> GetLanguage(Session session, ChatEntryId id, CancellationToken cancellationToken);
}
