using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class LanguageUI
{
    public ISyncedState<LanguageUserSettings> Languages { get; }
    private IJSRuntime JS { get; }

    public LanguageUI(AccountSettings accountSettings, IStateFactory stateFactory, IJSRuntime js)
    {
        JS = js;
        Languages = stateFactory.NewKvasSynced<LanguageUserSettings>(
            new (accountSettings, LanguageUserSettings.KvasKey) {
                InitialValueFactory = CreateLanguageSettings,
                PersistInitialValue = true,
            });
    }

    private async Task<LanguageUserSettings> CreateLanguageSettings(CancellationToken cancellationToken)
    {
        var languages = await ListClientLanguages(cancellationToken);
        return new () {
            Primary = languages.FirstOrDefault(LanguageId.Default),
            Secondary = languages.ElementAtOrDefault(1),
        };
    }

    private async Task<LanguageId[]> ListClientLanguages(CancellationToken cancellationToken)
    {
        var supportedLanguages = new Dictionary<string, LanguageId>(StringComparer.Ordinal);
        foreach (var languageId in LanguageId.All)
        {
            supportedLanguages[languageId.Shortcut] = languageId;
            supportedLanguages[languageId.Value] = languageId;
        }

        var browserLanguages = await JS
            .InvokeAsync<string[]>($"{ChatBlazorUIModule.ImportName}.getLanguages", cancellationToken)
            .ConfigureAwait(false);
        return browserLanguages.Select(x => supportedLanguages.GetValueOrDefault(x.ToUpperInvariant()))
            .Where(x => x.IsValid)
            .Distinct()
            .ToArray();
    }

    public async Task<LanguageId> NextLanguage(LanguageId language, CancellationToken cancellationToken = default)
    {
        var languages = await Languages.Use(cancellationToken).ConfigureAwait(false);
        return languages.Next(language);
    }
}
