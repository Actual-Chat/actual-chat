using ActualChat.Concurrency;
using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Owns <see cref="Language"/> - the language the app renders in, pulled once at startup - and
/// localizes server-composed message text (e.g. error/exception messages that cross RPC) into it:
/// catalogued messages resolve through <see cref="MessageIndex"/>, the rest fall back to AI
/// translation behind <see cref="ITranslations.GetTranslatedUIText"/>, whose compute cache dedups
/// them; concurrent calls are throttled by a <see cref="ConcurrentProcessor{TKey,TResult}"/>.
/// </summary>
public class LocalizationUI : UIServiceBase<AppUIHub>, IUITextLocalizer, IComputeService, IAsyncDisposable
{
    private const int ConcurrencyLevel = 10;
    private const string JSSetDocumentLanguageMethod = "window.LocalizationUI.setDocumentLanguage";

    private readonly ConcurrentProcessor<Key, string?> _localizations;
    private readonly AsyncTaskMethodBuilder _whenReadySource = AsyncTaskMethodBuilderExt.New();

    private ITranslations Translator => Hub.Translations;
    IServiceProvider IHasServices.Services => Hub.Services;

    // A plain property rather than a state: the localizer reads it synchronously, and its value
    // stays fixed for the lifetime of the app instance.
    public Language Language { get; private set; } = Languages.Main;
    public Task WhenReady => _whenReadySource.Task;

    public LocalizationUI(AppUIHub hub) : base(hub)
        => _localizations = new(ConcurrencyLevel, Localize, log: hub.LogFor<ConcurrentProcessor<Key, string?>>());

    public ValueTask DisposeAsync()
        => _localizations.DisposeSilentlyAsync($"{GetType().GetName()}._localizations", Log);

    public void SetLanguage(Language language)
    {
        if (WhenReady.IsCompleted)
            return;

        Language = language;
        _whenReadySource.TrySetResult();
        // Not awaited: <html lang> is cosmetic next to the render that's about to happen,
        // and this runs on the dispatcher right before the first render.
        _ = JS.InvokeVoidAsync(JSSetDocumentLanguageMethod, language.Value);
    }

    [ComputeMethod]
    public virtual async Task<string> Get(string message, CancellationToken cancellationToken = default)
    {
        if (message.IsNullOrWhiteSpace())
            return message;

        await WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        var language = Language;
        if (language.IsAnyEnglish)
            return message;

        var localized = L.ForRuntimeMessage(message);
        if (localized != null)
            return localized;
        if (language == Languages.Max)
            return message;

        // A null means the call never got its connection, and this renders English until the next
        // one invalidates it. The computed is captured before the call and used with
        // allowInconsistent, so a reconnect racing the timeout invalidates it too - a plain Use()
        // would re-read it as "connected" and hold English until the connection drops again.
        var isConnectedComputed = Hub.ConnectivityUI.IsConnected.Computed;
        var item = _localizations.Enqueue(new Key(language, message));
        var translated = await item.ResultTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (translated != null)
            return translated;

        await isConnectedComputed.Use(allowInconsistent: true, cancellationToken).ConfigureAwait(false);
        return message;
    }

    // Private methods

    private async Task<string?> Localize(Key key, CancellationToken cancellationToken)
    {
        // null = no connection to make the call on - at once offline, within ConnectTimeout while the
        // server is down. Faulting the item would log an error per call and, the exception being
        // transient, repeat that every second.
        try {
            var translated = await Translator
                .GetTranslatedUIText(Session, key.Message, key.Language, UITextKind.ErrorMessage, cancellationToken)
                .ConfigureAwait(false);
            return translated.NullIfEmpty() ?? key.Message;
        }
        catch (RpcTimeoutException e) when (e.TimeoutKind == RpcTimeoutKind.Connect) {
            return null;
        }
    }

    // Nested types

    private sealed record Key(Language Language, string Message);
}
