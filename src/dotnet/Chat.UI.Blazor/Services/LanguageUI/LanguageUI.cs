using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class LanguageUI
{
    public ISyncedState<UserLanguageSettings> Languages { get; }
    private IJSRuntime JS { get; }

    public LanguageUI(
        AccountSettings accountSettings,
        IStateFactory stateFactory,
        IJSRuntime js)
    {
        JS = js;
        Languages = stateFactory.NewKvasSynced<UserLanguageSettings>(
            new (accountSettings, UserLanguageSettings.KvasKey) {
                InitialValueFactory = CreateLanguageSettings,
                MustWriteInitialValue = true,
            });
    }

    public async Task<LanguageId> NextLanguage(LanguageId language, CancellationToken cancellationToken = default)
    {
        var languages = await Languages.Use(cancellationToken).ConfigureAwait(false);
        return languages.Next(language);
    }

    private async Task<UserLanguageSettings> CreateLanguageSettings(CancellationToken cancellationToken)
    {
        var languages = await GetClientLanguages(cancellationToken);
        return new () {
            Primary = languages.Count > 0 ? languages[0] : LanguageId.Default,
            Secondary = languages.Count > 1 ? (LanguageId?) languages[1] : null,
        };
    }

    private async ValueTask<List<LanguageId>> GetClientLanguages(CancellationToken cancellationToken)
    {
        var browserLanguages = await JS
            .InvokeAsync<string[]>($"{ChatBlazorUIModule.ImportName}.LanguageUI.getLanguages", cancellationToken)
            .ConfigureAwait(false);
        return browserLanguages
            .Select(x => LanguageId.Map.GetValueOrDefault(x))
            .Where(x => x.IsValid)
            .Distinct()
            .ToList();
    }
}
