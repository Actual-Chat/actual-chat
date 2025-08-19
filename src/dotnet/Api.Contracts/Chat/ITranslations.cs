namespace ActualChat.Chat;

public interface ITranslations : IComputeService
{
    [Obsolete("2025.08: Use Get with translateIfMissing flag instead.")]
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Translation?> Get(Session session, TranslationId id, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 60)]
    Task<Translation?> Get(Session session, TranslationId id, bool translateIfMissing, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<ChatLanguageTile> GetLanguageTile(
        Session session,
        ChatId chatId,
        Range<long> idTileRange,
        CancellationToken cancellationToken);
}
