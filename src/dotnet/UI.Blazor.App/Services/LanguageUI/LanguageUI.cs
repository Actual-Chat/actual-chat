using ActualChat.UI.Blazor.App.Module;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Manages the user's spoken/transcription language settings (per-user and per-chat) and
/// the device-local UI display language read by the string localizer.
/// </summary>
public class LanguageUI : UIServiceBase<AppUIHub>, IComputeService, IDisposable
{
    private static readonly string JSGetLanguagesMethod = $"{BlazorUIAppModule.ImportName}.LanguageUI.getLanguages";
    public static readonly string[] SupportedUILanguages = ["en", "es", "fr", "it", "ru", "de"];
    public static readonly string DefaultUILanguage = Languages.Main.ShortTitle.ToLowerInvariant();
    private static readonly HashSet<string> SupportedUILanguageSet = [..SupportedUILanguages];

    public SyncedState<UserLanguageSettings> Settings { get; init; }
    public SyncedState<string> UILanguage { get; init; }
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
        UILanguage = StateFactory.NewKvasSynced<string>(
            new(hub.LocalSettings, nameof(UILanguage)) {
                InitialValue = "",
                Category = StateCategories.Get(GetType(), nameof(UILanguage)),
                // TODO: missingvaluefactory
            });
        _ = EnsureUserLanguageSettingsPersisted();
        _ = DetectUILanguage();
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
    {
        Settings.Dispose();
        UILanguage.Dispose();
    }

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

    public async Task SetUILanguage(string? language, CancellationToken cancellationToken = default)
    {
        await UILanguage.WhenFirstTimeRead.ConfigureAwait(false);
        UILanguage.Value = NormalizeUILanguage(language);
        // The setter only schedules the write; wait for it to persist so it survives the reload the caller triggers next.
        await UILanguage.WhenWritten(cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private static bool IsSupportedUILanguage(string? language)
        => language != null && Languages.ById.ContainsKey(language) && SupportedUILanguageSet.Contains(language);

    private static string NormalizeUILanguage(string? language)
        => IsSupportedUILanguage(language) ? language! : DefaultUILanguage;

    private async Task DetectUILanguage()
    {
        await Hub.Services.GetRequiredService<BrowserInfo>().WhenReady.ConfigureAwait(false);
        var languages = await GetClientLanguages(default).ConfigureAwait(false);
        foreach (var language in languages) {
            var code = language.Value.Split('-')[0].ToLower();
            if (IsSupportedUILanguage(code)) {
                _detectedUILanguage = code;
                return;
            }
        }
    }

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
