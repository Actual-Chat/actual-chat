namespace ActualChat.Chat;

/// <summary>
/// Frontend service for retrieving chat entry translations.
/// </summary>
public class Translations(IServiceProvider services) : ITranslations
{
    private IServiceProvider Services { get; } = services;
    private ITranslationsBackend Backend => field ??= Services.GetRequiredService<ITranslationsBackend>();
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private IChatEntryLanguagesBackend ChatEntryLanguagesBackend
        => field ??= Services.GetRequiredService<IChatEntryLanguagesBackend>();

    // [ComputeMethod]
    public virtual async Task<Translation?> Get(
        Session session,
        TranslationId id,
        bool translateIfMissing,
        CancellationToken cancellationToken)
    {
        _ = await Chats.Get(session, id.SourceId.ChatId, cancellationToken).Require().ConfigureAwait(false);
        return await Backend.Get(id, translateIfMissing, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ChatLanguageTile> GetLanguageTile(
        Session session,
        ChatId chatId,
        Range<long> lidTileRange,
        CancellationToken cancellationToken)
    {
        _ = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        return await ChatEntryLanguagesBackend.GetTile(chatId, lidTileRange, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<string?> GetTranslatedUIText(
        Session session,
        string text,
        Language language,
        UITextKind kind,
        CancellationToken cancellationToken)
    {
        if (text.IsNullOrWhiteSpace() || language.IsAnyEnglish)
            return text;
        if (text.Length > Constants.Translation.MaxTextTranslationLength)
            return null;

        return await Backend.GetTranslatedUIText(text, language, kind, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<DubVoice>> ListDubVoices(Session session, CancellationToken cancellationToken)
    {
        await Accounts.GetOwn(session, cancellationToken).Require(AccountFull.MustBeActive).ConfigureAwait(false);
        return await Backend.ListDubVoices(cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<byte[]?> GetDubVoicePreview(
        Session session,
        string voiceId,
        Language language,
        CancellationToken cancellationToken)
    {
        await Accounts.GetOwn(session, cancellationToken).Require(AccountFull.MustBeActive).ConfigureAwait(false);
        return await Backend.GetDubVoicePreview(voiceId, language, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<DubVoice>> ListSuggestedDubVoices(
        Session session,
        CancellationToken cancellationToken)
    {
        var voices = await ListDubVoices(session, cancellationToken).ConfigureAwait(false);
        if (voices.Count == 0)
            return voices;

        var settings = await Services.UserSettingsUI(session)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        return DubVoiceAccents.Suggest(voices, settings.ListSpoken()).ToApiArray();
    }
}
