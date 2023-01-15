using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class LanguageUI
{
    private AccountSettings AccountSettings { get; }
    private Dispatcher Dispatcher { get; }
    private IJSRuntime JS { get; }

    public ISyncedState<UserLanguageSettings> Settings { get; }

    public LanguageUI(IServiceProvider services)
    {
        Dispatcher = services.GetRequiredService<Dispatcher>();
        JS = services.GetRequiredService<IJSRuntime>();

        var stateFactory = services.StateFactory();
        AccountSettings = services.GetRequiredService<AccountSettings>();
        Settings = stateFactory.NewKvasSynced<UserLanguageSettings>(
            new (AccountSettings, UserLanguageSettings.KvasKey) {
                MissingValueFactory = CreateLanguageSettings,
                UpdateDelayer = FixedDelayer.Instant,
            });
    }

    public async ValueTask<Language> GetChatLanguage(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        return await userChatSettings.LanguageOrPrimary(AccountSettings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Language> ChangeChatLanguage(ChatId chatId)
    {
        await Settings.WhenFirstTimeRead.ConfigureAwait(false);
        var settings = Settings.Value;
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, default).ConfigureAwait(false);
        var language = userChatSettings.Language.Or(settings.Primary);
        language = settings.Next(language);
        if (language == userChatSettings.Language)
            return language;

        userChatSettings = userChatSettings with { Language = language };
        await AccountSettings.SetUserChatSettings(chatId, userChatSettings, default).ConfigureAwait(false);
        return language;
    }

    // Private methods

    private async ValueTask<UserLanguageSettings> CreateLanguageSettings(CancellationToken cancellationToken)
    {
        var languages = await GetClientLanguages(cancellationToken);
        return new () {
            Primary = languages.Count > 0 ? languages[0] : Languages.Main,
            Secondary = languages.Count > 1 ? (Language?) languages[1] : null,
        };
    }

    private async ValueTask<List<Language>> GetClientLanguages(CancellationToken cancellationToken)
    {
        var browserLanguages = await Dispatcher.InvokeAsync(
            () => JS.InvokeAsync<string[]>(
                $"{ChatBlazorUIModule.ImportName}.LanguageUI.getLanguages",
                cancellationToken).AsTask());
        return browserLanguages
            .Select(x => new Language(x, ParseOrNone.Option))
            .Where(x => !x.IsNone)
            .Distinct()
            .ToList();
    }
}
