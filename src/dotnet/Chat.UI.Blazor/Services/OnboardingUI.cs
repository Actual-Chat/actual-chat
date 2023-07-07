using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class OnboardingUI
{
    private readonly ISyncedState<UserOnboardingSettings> _settings;

    private Session Session { get; }
    private IAccounts Accounts { get; }
    private AccountSettings AccountSettings { get; }
    private ModalUI ModalUI { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public IState<UserOnboardingSettings> Settings => _settings;

    public OnboardingUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        AccountSettings = services.GetRequiredService<AccountSettings>();
        ModalUI = services.GetRequiredService<ModalUI>();
        Clocks = services.Clocks();

        var stateFactory = services.StateFactory();
        _settings = stateFactory.NewKvasSynced<UserOnboardingSettings>(
            new (AccountSettings, UserOnboardingSettings.KvasKey) {
                InitialValue = new UserOnboardingSettings(),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
    }

    public async ValueTask TryShow()
    {
        if (!await ShouldBeShown())
            return;

        UpdateSettings(Settings.Value with { LastShownAt = Now });
        await ModalUI.Show(new OnboardingModal.Model());
    }

    public void UpdateSettings(UserOnboardingSettings value)
        => _settings.Value = value;

    // Private methods

    private async Task<bool> ShouldBeShown()
    {
        // 1. Wait for sign-in
        try {
            await Computed
                .Capture(() => Accounts.GetOwn(Session, CancellationToken.None))
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

        // 4.Wait when settings migrated
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
