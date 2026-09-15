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

    [ComputeMethod(MinCacheDuration = 300), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<string?> GetTranslatedUIText(
        Session session,
        string text,
        Language language,
        UITextKind kind,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 600), RemoteComputeMethod(MinCacheDuration = 3600)]
    Task<ApiArray<DubVoice>> ListDubVoices(Session session, CancellationToken cancellationToken);
    // The catalog's voices matching the accents of the languages the user speaks (DubVoiceAccents.Suggest)
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 60)]
    Task<ApiArray<DubVoice>> ListSuggestedDubVoices(Session session, CancellationToken cancellationToken);
}
