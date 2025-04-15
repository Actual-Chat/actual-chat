using ActualChat.UI.Blazor.App.Module;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class LanguageUI : ScopedServiceBase<ChatUIHub>, IComputeService, IDisposable
{
    private static readonly string JSGetLanguagesMethod = $"{BlazorUIAppModule.ImportName}.LanguageUI.getLanguages";
    private readonly SyncedState<UserLanguageSettings> _settings;

    private TuneUI TuneUI => Hub.TuneUI;
    private IJSRuntime JS => Hub.JSRuntime();

    public IState<UserLanguageSettings> Settings => _settings;

    public LanguageUI(ChatUIHub hub) : base(hub)
        => _settings = StateFactory.NewKvasSynced<UserLanguageSettings>(
            new (AccountSettings, UserLanguageSettings.KvasKey) {
                InitialValue = new UserLanguageSettings(),
                MissingValueFactory = CreateLanguageSettings,
                UpdateDelayer = FixedDelayer.NextTick,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });

    public void Dispose()
        => _settings.Dispose();

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<Language>> ListSpokenLanguages(CancellationToken cancellationToken)
    {
        var settings = await Settings.Use(cancellationToken).ConfigureAwait(false);
        return settings.AllSpoken;
    }

    [ComputeMethod]
    public virtual async Task<Language> GetChatLanguage(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        return await userChatSettings.LanguageOrPrimary(AccountSettings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Language> ChangeChatLanguage(ChatId chatId, Language language, CancellationToken cancellationToken = default)
    {
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        if (language == userChatSettings.Language)
            return language;

        _ = TuneUI.Play(Tune.ChangeLanguage);
        userChatSettings = userChatSettings with { Language = language };
        await AccountSettings.SetUserChatSettings(chatId, userChatSettings, cancellationToken).ConfigureAwait(false);
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
            .Select(Language.TryParse)
            .SkipNullItems()
            .Distinct()
            .ToList();
    }
}
