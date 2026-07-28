using ActualChat.Concurrency;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Localizes server-composed message text (e.g. error/exception messages that cross RPC) into the
/// device UI language via AI translation; dedup/caching is handled by the compute cache behind
/// <see cref="ITranslations.GetTranslatedUIText"/>, concurrent calls are throttled and deduplicated
/// by a <see cref="ConcurrentProcessor{TKey,TResult}"/>.
/// </summary>
public class LocalizationUI : UIServiceBase<AppUIHub>, IComputeService, IAsyncDisposable
{
    private const int ConcurrencyLevel = 10;
    private readonly ConcurrentProcessor<Key, string> _localizations;

    private ITranslations Translator => Hub.Translations;
    private LanguageUI LanguageUI => Hub.LanguageUI;

    public LocalizationUI(AppUIHub hub) : base(hub)
        => _localizations = new(ConcurrencyLevel, Localize, log: hub.LogFor<ConcurrentProcessor<Key, string>>());

    public ValueTask DisposeAsync()
        => _localizations.DisposeSilentlyAsync($"{GetType().GetName()}._localizations", Log);

    [ComputeMethod]
    public virtual async Task<string> Get(string message, CancellationToken cancellationToken = default)
    {
        if (message.IsNullOrWhiteSpace())
            return message;

        var language = await LanguageUI.UILanguage.Use(cancellationToken).ConfigureAwait(false);
        if (language.IsAnyEnglish)
            return message;

        var item = _localizations.Enqueue(new Key(language, message));
        return await item.ResultTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<string> Localize(Key key, CancellationToken cancellationToken)
    {
        var translated = await Translator
            .GetTranslatedUIText(Session, key.Message, key.Language, UITextKind.ErrorMessage, cancellationToken)
            .ConfigureAwait(false);
        return translated.NullIfEmpty() ?? key.Message;
    }

    // Nested types

    private sealed record Key(Language Language, string Message);
}
