using ActualLab.Rpc;

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

    // Without it the call parks until the reconnect, and an error toast waits out its own 10s timeout;
    // a cached translation is served before this ever applies.
    [RpcMethod(ConnectTimeout = 5)]
    [ComputeMethod(MinCacheDuration = 300), RemoteComputeMethod(MinCacheDuration = 300)]
    Task<string?> GetTranslatedUIText(
        Session session,
        string text,
        Language language,
        UITextKind kind,
        CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 600), RemoteComputeMethod(MinCacheDuration = 3600)]
    Task<ApiArray<DubVoice>> ListDubVoices(Session session, CancellationToken cancellationToken);
    // MP3 of a short localized sentence in the given voice; null when the voice is unknown
    [ComputeMethod(MinCacheDuration = 3600), RemoteComputeMethod(MinCacheDuration = 3600)]
    Task<byte[]?> GetDubVoicePreview(Session session, string voiceId, Language language, CancellationToken cancellationToken);
    // The catalog's voices matching the accents of the languages the user speaks (DubVoiceAccents.Suggest)
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 60)]
    Task<ApiArray<DubVoice>> ListSuggestedDubVoices(Session session, CancellationToken cancellationToken);
}
