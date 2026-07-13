using ActualChat.UI.Blazor.App.Module;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Manages the user's spoken/transcription language settings (per-user and per-chat) and
/// the client-only UI display language read by the string localizer.
/// </summary>
public class LanguageUI : UIServiceBase<AppUIHub>, IComputeService, IDisposable
{
    private static readonly string JSGetLanguagesMethod = $"{BlazorUIAppModule.ImportName}.LanguageUI.getLanguages";

    private UILanguageState UILanguageState => field ??= Hub.Services.GetRequiredService<UILanguageState>();
    public SyncedState<UserLanguageSettings> Settings { get; init; }
    public Task WhenReady => Settings.WhenFirstTimeRead;

    public LanguageUI(AppUIHub hub) : base(hub)
    {
        Settings = StateFactory.NewUserSettingsSynced(
            UserSettingsUI,
            UserLanguageSettings.KvasKey,
            new UserLanguageSettings(),
            updateDelayer: FixedDelayer.NextTick,
            missingValueFactory: CreateLanguageSettings,
            category: StateCategories.Get(GetType(), nameof(Settings)));
        _ = EnsureUserLanguageSettingsPersisted();
    }

    private async Task EnsureUserLanguageSettingsPersisted(CancellationToken cancellationToken = default)
    {
        await WhenReady.ConfigureAwait(false);
        var serverValue = await UserSettingsUI.Get(UserLanguageSettings.KvasKey, cancellationToken).ConfigureAwait(false);
        if (serverValue is not null)
            return;

        await UserSettingsUI.Set(UserLanguageSettings.KvasKey, Settings.Value, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
        => Settings.Dispose();

    [ComputeMethod]
    public virtual async Task<IReadOnlyList<Language>> ListSpoken(CancellationToken cancellationToken)
    {
        var settings = await Settings.Use(WhenReady, cancellationToken).ConfigureAwait(false);
        return settings.ListSpoken();
    }

    [ComputeMethod]
    public virtual async Task<Language> GetChatLanguage(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        var (chatLanguage, primary) = await GetChatLanguageAndPrimary(chatId, cancellationToken).ConfigureAwait(false);
        return chatLanguage ?? primary;
    }

    [ComputeMethod]
    public virtual async Task<bool> IsChatLanguageSelected(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var (chatLanguage, _) = await GetChatLanguageAndPrimary(chatId, cancellationToken).ConfigureAwait(false);
        return chatLanguage is not null;
    }

    [ComputeMethod]
    public virtual async Task<(Language?, Language)> GetChatLanguageAndPrimary(ChatId? chatId, CancellationToken cancellationToken = default)
    {
        chatId = chatId?.GetThreadOutermostParentOrSelf();
        var chatUserSettings = chatId is not null
            ? await UserSettingsUI.ChatUserSettings(chatId).Get(cancellationToken).ConfigureAwait(false)
            : ChatUserSettings.Default;
        var language = chatUserSettings.Language;
        var userSettings = await Settings.Use(WhenReady, cancellationToken).ConfigureAwait(false);
        return (language, userSettings.Primary);
    }

    public async Task<Language> ChangeChatLanguage(ChatId chatId, Language language, CancellationToken cancellationToken = default)
    {
        chatId = chatId.GetThreadOutermostParentOrSelf();
        await WhenReady.ConfigureAwait(false);
        var chatUserSettings = await UserSettingsUI.ChatUserSettings(chatId).Get(cancellationToken).ConfigureAwait(false);
        if (language == chatUserSettings.Language)
            return language;

        _ = TuneUI.Play(Tune.ChangeLanguage);
        chatUserSettings = chatUserSettings with { Language = language };
        await UserSettingsUI.ChatUserSettings(chatId).Set(chatUserSettings, cancellationToken).ConfigureAwait(false);
        return language;
    }

    public async Task UpdateSettings(Func<UserLanguageSettings, UserLanguageSettings> updater)
    {
        await WhenReady.ConfigureAwait(false);
        Settings.Set(updater.Invoke(Settings.Value));
    }

    public async Task InitializeUILanguage(CancellationToken cancellationToken = default)
    {
        // TODO: store in UserLanguageSettings
        var stored = await Hub.LocalSettings.Get<string>(UILanguageState.KvasKey, cancellationToken).ConfigureAwait(false);
        UILanguageState.Language = UILanguageState.IsSupported(stored)
            ? stored!
            : await DetectDefaultUILanguage(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetUILanguage(string? language, CancellationToken cancellationToken = default)
    {
        var normalized = UILanguageState.Normalize(language);
        UILanguageState.Language = normalized;
        var localSettings = Hub.LocalSettings;
        await localSettings.Set(UILanguageState.KvasKey, normalized, cancellationToken).ConfigureAwait(false);
        // Set() only enqueues the batched write; flush so it survives the reload the caller triggers next.
        await localSettings.Flush(cancellationToken).ConfigureAwait(false);
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

    private async Task<string> DetectDefaultUILanguage(CancellationToken cancellationToken)
    {
        var clientLanguages = await GetClientLanguages(cancellationToken).ConfigureAwait(false);
        foreach (var language in clientLanguages) {
            var code = language.Value.Split('-')[0].ToLower();
            if (UILanguageState.IsSupported(code))
                return code;
        }
        return UILanguageState.DefaultLanguage;
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
