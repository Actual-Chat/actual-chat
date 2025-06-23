namespace ActualChat.Chat;

public interface ITranslations : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Translation?> Get(Session session, TranslationId id, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<ChatLanguageTile> GetLanguageTile(
        Session session,
        ChatId chatId,
        ChatEntryKind entryKind,
        Range<long> idTileRange,
        CancellationToken cancellationToken);
}
