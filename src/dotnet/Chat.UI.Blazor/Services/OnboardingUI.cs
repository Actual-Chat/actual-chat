using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class OnboardingUI
{
    private readonly ISyncedState<UserOnboardingSettings> _settings;

    private Session Session { get; }
    private IAccounts Accounts { get; }
    private ModalUI ModalUI { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public IState<UserOnboardingSettings> Settings => _settings;

    public OnboardingUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        ModalUI = services.GetRequiredService<ModalUI>();
        Clocks = services.GetRequiredService<MomentClockSet>();

        var stateFactory = services.StateFactory();
        var accountSettings = services.GetRequiredService<AccountSettings>();
        _settings = stateFactory.NewKvasSynced<UserOnboardingSettings>(
            new (accountSettings, UserOnboardingSettings.KvasKey) {
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

    private async ValueTask<bool> ShouldBeShown()
    {
        // Uncomment to debug OnboardingUI:
        // UpdateSettings(new());

        try {
            await Computed
                .Capture(() => Accounts.GetOwn(Session, CancellationToken.None))
                .When(x => !x.IsGuestOrNone, Clocks.Timeout(3));
            await Task.Delay(TimeSpan.FromSeconds(0.1));
        }
        catch (TimeoutException) {
            return false;
        }

        await _settings.WhenFirstTimeRead;
        var settings = _settings.Value;

        // NOTE(AY): We should stick to the typical UX as much as we can, otherwise we won't see the bugs
        // if (account.IsAdmin && settings.LastShownAt is { } lastShownAt && lastShownAt + TimeSpan.FromDays(1) < Now)
        //     return false;

        return settings.HasUncompletedSteps;
    }
}
