namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Localizes server-composed message text (e.g. error/exception messages that cross RPC) into the
/// device UI language via AI translation; dedup/caching is handled by the compute cache behind
/// <see cref="ITranslations.GetTranslatedUIText"/>.
/// </summary>
public sealed class LocalizationUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private ITranslations Translator => Hub.Translations;
    private LanguageUI LanguageUI => Hub.LanguageUI;

    public async Task<string> Get(string message, CancellationToken cancellationToken = default)
    {
        if (message.IsNullOrWhiteSpace())
            return message;

        var session = Session;
        var translator = Translator;
        // Instance properties are cached, so .ConfigureAwait(false) is fine from here

        var language = await LanguageUI.UILanguage.Use(cancellationToken).ConfigureAwait(false);
        if (language.IsAnyEnglish)
            return message;

        var translated = await translator
            .GetTranslatedUIText(session, message, language, UITextKind.ErrorMessage, cancellationToken)
            .ConfigureAwait(false);
        return translated.NullIfEmpty() ?? message;
    }
}
