using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Owns <see cref="Settings"/> - the account's spoken languages and its UI language - and mirrors
/// the UI language into the device cache.
/// </summary>
public class LanguageUI : UIWorkerBase<AppUIHub>, IComputeService, IDisposable
{
    private const string JSSetMethod = "window.LocalizationUI.set";
    private BrowserInfo BrowserInfo => Hub.BrowserInfo;

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

    public void Dispose()
        => Settings.DisposeSilently();

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
    public virtual async Task<(Language?, Language)> GetChatLanguageAndPrimary(
        ChatId? chatId,
        CancellationToken cancellationToken = default)
    {
        chatId = chatId?.GetThreadOutermostParentOrSelf();
        var chatUserSettings = chatId is not null
            ? await UserSettingsUI.ChatUserSettings(chatId).Get(cancellationToken).ConfigureAwait(false)
            : ChatUserSettings.Default;
        var language = chatUserSettings.Language;
        var userSettings = await Settings.Use(WhenReady, cancellationToken).ConfigureAwait(false);
        return (language, userSettings.Primary);
    }

    public async Task<Language> ChangeChatLanguage(
        ChatId chatId,
        Language language,
        CancellationToken cancellationToken = default)
    {
        chatId = chatId.GetThreadOutermostParentOrSelf();
        await WhenReady.ConfigureAwait(false);
        var chatUserSettings = await UserSettingsUI.ChatUserSettings(chatId)
            .Get(cancellationToken)
            .ConfigureAwait(false);
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

    public async Task UpdateUILanguage(Language? language)
    {
        await UpdateSettings(x => x with { UILanguage = language }).ConfigureAwait(false);
        await SetStoredLanguage(language).ConfigureAwait(false);
    }

    // Protected/internal methods

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return AsyncChain.From(SyncUILanguage)
            .Log(LogLevel.Debug, Log)
            .RetryForever(retryDelays, Log)
            .RunIsolated(cancellationToken);
    }

    // Private methods

    private async Task SyncUILanguage(CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
        var changes = Settings.Computed.Changes(cancellationToken);
        await foreach (var (settings, _) in changes.ConfigureAwait(false))
            await SetStoredLanguage(settings.UILanguage).ConfigureAwait(false);
    }

    private async Task SetStoredLanguage(Language? language)
        => await JS.InvokeVoidAsync(JSSetMethod, language?.Value).ConfigureAwait(false);

    private async ValueTask<UserLanguageSettings> CreateLanguageSettings(CancellationToken cancellationToken)
    {
        await BrowserInfo.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        var languages = BrowserInfo.ClientLanguages
            .Select(x => Language.TryParse(x))
            .SkipNullItems()
            .Distinct()
            .ToList();
        return new () {
            Primary = languages.Count > 0 ? languages[0] : Languages.Main,
            Secondary = languages.Count > 1 ? (Language?) languages[1] : null,
            Tertiary = languages.Count > 2 ? (Language?) languages[2] : null,
        };
    }

    private async Task EnsureUserLanguageSettingsPersisted(CancellationToken cancellationToken = default)
    {
        await WhenReady.ConfigureAwait(false);
        var serverValue = await UserSettingsUI
            .Get(UserLanguageSettings.KvasKey, cancellationToken)
            .ConfigureAwait(false);
        if (serverValue is not null)
            return;

        await UserSettingsUI.Set(UserLanguageSettings.KvasKey, Settings.Value, cancellationToken).ConfigureAwait(false);
    }
}
