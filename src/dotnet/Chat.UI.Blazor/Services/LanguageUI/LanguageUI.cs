using ActualChat.Chat.UI.Blazor.Module;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class LanguageUI : ScopedServiceBase, IDisposable
{
    private static readonly string JSGetLanguagesMethod = $"{ChatBlazorUIModule.ImportName}.LanguageUI.getLanguages";
    private readonly ISyncedState<UserLanguageSettings> _settings;
    private TuneUI? _tuneUI;
    private IJSRuntime? _js;

    private AccountSettings AccountSettings { get; }
    private TuneUI TuneUI => _tuneUI ??= Services.GetRequiredService<TuneUI>();
    private IJSRuntime JS => _js ??= Services.JSRuntime();

    public IState<UserLanguageSettings> Settings => _settings;

    public LanguageUI(IServiceProvider services) : base(services)
    {
        AccountSettings = services.GetRequiredService<AccountSettings>();
        _settings = StateFactory.NewKvasSynced<UserLanguageSettings>(
            new (AccountSettings, UserLanguageSettings.KvasKey) {
                InitialValue = new UserLanguageSettings(),
                MissingValueFactory = CreateLanguageSettings,
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
    }

    public void Dispose()
        => _settings.Dispose();

    public async ValueTask<Language> GetChatLanguage(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        return await userChatSettings.LanguageOrPrimary(AccountSettings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Language> ChangeChatLanguage(ChatId chatId, Language language = default)
    {
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);
        var settings = Settings.Value;
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, default).ConfigureAwait(false);
        var oldLanguage = userChatSettings.Language.Or(settings.Primary);
        language = language.Or(oldLanguage);
        if (language == userChatSettings.Language)
            return language;

        _ = TuneUI.Play(Tune.ChangeLanguage);
        userChatSettings = userChatSettings with { Language = language };
        await AccountSettings.SetUserChatSettings(chatId, userChatSettings, default).ConfigureAwait(false);
        return language;
    }

    public void UpdateSettings(UserLanguageSettings value)
        => _settings.Value = value;

    // Private methods

    private async ValueTask<UserLanguageSettings> CreateLanguageSettings(CancellationToken cancellationToken)
    {
        var languages = await GetClientLanguages(cancellationToken).ConfigureAwait(false);
        var settings = new UserLanguageSettings() {
            Primary = languages.Count > 0 ? languages[0] : Languages.Main,
            Secondary = languages.Count > 1 ? (Language?) languages[1] : null,
        };

        // This code stores the languages after 1s delay
        _ = BackgroundTask.Run(async () => {
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None).ConfigureAwait(false);
            _settings.Set(_ => settings);
        }, CancellationToken.None);
        return settings;
    }

    private async ValueTask<List<Language>> GetClientLanguages(CancellationToken cancellationToken)
    {
        var languages = await JS.InvokeAsync<string[]>(JSGetLanguagesMethod, CancellationToken.None)
            .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        return languages
            .Select(x => new Language(x, ParseOrNone.Option))
            .Where(x => !x.IsNone)
            .Distinct()
            .ToList();
    }
}
