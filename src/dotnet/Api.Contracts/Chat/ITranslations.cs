namespace ActualChat.Chat;

/// <summary>
/// Service for translating chat messages to different languages.
/// </summary>
public interface ITranslations : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Translation?> Get(Session session, TranslationId id, bool translateIfMissing, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 10), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<ChatLanguageTile> GetLanguageTile(
        Session session,
        ChatId chatId,
        Range<long> lidTileRange,
        CancellationToken cancellationToken);

    // TODO: better name?
    // TODO: think how to pass some context to make translation more accurate
    [ComputeMethod(MinCacheDuration = 300), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<string?> TranslateText(Session session, string text, Language language, CancellationToken cancellationToken);
}
