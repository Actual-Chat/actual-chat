using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class OnboardingUI : IDisposable
{
    private readonly ISyncedState<UserOnboardingSettings> _settings;

    private IServiceProvider Services { get; }
    private AccountUI AccountUI { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public IState<UserOnboardingSettings> Settings => _settings;

    public OnboardingUI(IServiceProvider services)
    {
        Services = services;
        AccountUI = services.GetRequiredService<AccountUI>();
        Clocks = services.Clocks();

        var stateFactory = services.StateFactory();
        var accountSettings = services.GetRequiredService<AccountSettings>();
        _settings = stateFactory.NewKvasSynced<UserOnboardingSettings>(
            new (accountSettings, UserOnboardingSettings.KvasKey) {
                InitialValue = new UserOnboardingSettings(),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
        AccountUI.OwnAccountChanged += OnOwnAccountChanged;
    }

    public void Dispose()
        => AccountUI.OwnAccountChanged -= OnOwnAccountChanged;

    public async ValueTask TryShow()
    {
        if (!await ShouldBeShown())
            return;

        UpdateSettings(Settings.Value with { LastShownAt = Now });
        var modalUI = Services.GetRequiredService<ModalUI>();
        await modalUI.Show(new OnboardingModal.Model());
    }

    public void UpdateSettings(UserOnboardingSettings value)
        => _settings.Value = value;

    // Private methods

    private void OnOwnAccountChanged(AccountFull account)
    {
        if (!account.IsGuestOrNone)
            _ = TryShow();
    }

    private async Task<bool> ShouldBeShown()
    {
        // 1. Wait for sign-in
        try {
            await AccountUI.WhenLoaded;
            await AccountUI.OwnAccount
                .When(x => !x.IsGuestOrNone, Clocks.Timeout(2))
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            return false;
        }

        // 2. Wait when settings are read
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);

        // 3. Extra delay - just in case Origin is somehow set for cached settings
        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        await _settings.Synchronize();

        // 4. Wait when settings migrated
        try {
            await _settings.Computed
                .When(x => !x.Origin.IsNullOrEmpty(), Clocks.Timeout(1))
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            return false;
        }

        var settings = _settings.Value;
        return settings.HasUncompletedSteps;
    }
}
