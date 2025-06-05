using ActualChat.UI.Blazor.App.Module;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class LanguageUI : UIServiceBase<AppUIHub>, IComputeService, IDisposable
{
    private static readonly string JSGetLanguagesMethod = $"{BlazorUIAppModule.ImportName}.LanguageUI.getLanguages";
    private readonly SyncedState<UserLanguageSettings> _settings;

    public IState<UserLanguageSettings> Settings => _settings;
    public Task WhenReady => _settings.WhenFirstTimeRead;

    public LanguageUI(AppUIHub hub) : base(hub)
        => _settings = StateFactory.NewKvasSynced<UserLanguageSettings>(
            new (AccountSettings, UserLanguageSettings.KvasKey) {
                MissingValueFactory = CreateLanguageSettings,
                UpdateDelayer = FixedDelayer.NextTick,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });

    public void Dispose()
        => _settings.Dispose();

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<Language>> ListSpoken(CancellationToken cancellationToken)
    {
        var settings = await Settings.Use(cancellationToken).ConfigureAwait(false);
        return settings.ListSpoken();
    }

    [ComputeMethod]
    public virtual async Task<Language> GetChatLanguage(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        var userChatSettings = chatId is not null
            ? await Temporals.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false)
            : UserChatSettings.Default;
        return await userChatSettings.LanguageOrPrimary(AccountSettings, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Language> ChangeChatLanguage(ChatId chatId, Language language, CancellationToken cancellationToken = default)
    {
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);
        var userChatSettings = await Temporals.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        if (language == userChatSettings.Language)
            return language;

        _ = TuneUI.Play(Tune.ChangeLanguage);
        userChatSettings = userChatSettings with { Language = language };
        await Temporals.SetUserChatSettings(chatId, userChatSettings, cancellationToken).ConfigureAwait(false);
        return language;
    }

    public async Task UpdateSettings(Func<UserLanguageSettings, UserLanguageSettings> updater)
    {
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);
        var settings = updater.Invoke(_settings.Value);
        _settings.Value = settings;
    }

    // Private methods

    private async ValueTask<UserLanguageSettings> CreateLanguageSettings(CancellationToken cancellationToken)
    {
        var languages = await GetClientLanguages(cancellationToken).ConfigureAwait(false);
        return new () {
            Primary = languages.Count > 0 ? languages[0] : Languages.Main,
            Secondary = languages.Count > 1 ? (Language?) languages[1] : null,
            Tertiary = languages.Count > 2 ? (Language?) languages[2] : null,
        };
    }

    private async ValueTask<List<Language>> GetClientLanguages(CancellationToken cancellationToken)
    {
        try {
            var languages = await JS.InvokeAsync<string[]>(JSGetLanguagesMethod, CancellationToken.None)
                .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            return languages
                .Select(s => Language.TryParse(s, true))
                .SkipNullItems()
                .Distinct()
                .ToList();
        }
        catch (InvalidOperationException e) {
            Log.LogWarning(e, "Failed to get languages from JS, returning empty list");
            return [];
        }
    }
}
