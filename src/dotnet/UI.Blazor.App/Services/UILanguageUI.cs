using ActualChat.Kvas;
using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Persists the UI language in local settings and seeds <see cref="UILanguageState"/> at startup,
/// defaulting to the browser/device language (same source as transcription) when nothing is stored.
/// </summary>
// TODO: let's think of a better name
// TODO: probably merge into existing LanguageUI
// TODO: reuse existing LanguageUI.GetClientLanguages()
public sealed class UILanguageUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub)
{
    private static readonly string JSGetLanguagesMethod = $"{BlazorUIAppModule.ImportName}.LanguageUI.getLanguages";

    private UILanguageState State => field ??= Hub.Services.GetRequiredService<UILanguageState>();

    public async Task Initialize(CancellationToken cancellationToken = default)
    {
        var stored = await Hub.LocalSettings.Get<string>(UILanguageState.KvasKey, cancellationToken).ConfigureAwait(false);
        State.Language = UILanguageState.IsSupported(stored)
            ? stored!
            : await DetectDefault(cancellationToken).ConfigureAwait(false);
    }

    public async Task Set(string? language, CancellationToken cancellationToken = default)
    {
        var normalized = UILanguageState.Normalize(language);
        State.Language = normalized;
        var localSettings = Hub.LocalSettings;
        await localSettings.Set(UILanguageState.KvasKey, normalized, cancellationToken).ConfigureAwait(false);
        // Set() only enqueues the batched write; flush so it survives the reload the caller triggers next.
        await localSettings.Flush(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<string> DetectDefault(CancellationToken cancellationToken)
    {
        try {
            var clientLanguages = await JS.InvokeAsync<string[]>(JSGetLanguagesMethod, cancellationToken).ConfigureAwait(false);
            foreach (var language in clientLanguages) {
                var code = language.Split('-')[0].ToLower();
                if (UILanguageState.IsSupported(code))
                    return code;
            }
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to detect client UI language, falling back to default");
        }
        return UILanguageState.DefaultLanguage;
    }
}
