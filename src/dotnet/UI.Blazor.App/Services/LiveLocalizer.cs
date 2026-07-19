using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

// Localizes server-composed messages (error/exception text crossing RPC) into the device UI
// language via the AI text-translation endpoint. Dedup/caching is handled by the Fusion compute
// cache behind ITranslations.TranslateText; a table-based deterministic path is added later
// (see docs/plans/server-strings-localization.md).

// TODO: let's try implement IServerMessageLocalizer in TranslationUI
public sealed class LiveLocalizer(AppUIHub hub) : ILiveLocalizer
{
    private Session Session => hub.Session;
    private ITranslations Translations => hub.Translations;
    private LanguageUI LanguageUI => hub.LanguageUI;

    public async Task<string> Localize(string message, CancellationToken cancellationToken = default)
    {
        if (message.IsNullOrWhiteSpace())
            return message;

        var language = await LanguageUI.UILanguage.Use(cancellationToken).ConfigureAwait(false);
        if (language.IsAnyEnglish)
            return message;

        var translated = await Translations.TranslateText(Session, message, language, cancellationToken).ConfigureAwait(false);
        return translated.NullIfEmpty() ?? message;
    }
}
