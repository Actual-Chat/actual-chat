using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class OnboardingUI : IDisposable, IOnboardingUI
{
    private readonly ISyncedState<UserOnboardingSettings> _userSettings;
    private readonly IStoredState<LocalOnboardingSettings> _localSettings;
    private CancellationTokenSource? _lastTryShowCts;
    private ModalRef? _lastModalRef;

    private IServiceProvider Services { get; }
    private AccountUI AccountUI { get; }
    private LoadingUI LoadingUI { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public IState<UserOnboardingSettings> UserSettings => _userSettings;
    public IState<LocalOnboardingSettings> LocalSettings => _localSettings;

    public OnboardingUI(IServiceProvider services)
    {
        Services = services;
        AccountUI = services.GetRequiredService<AccountUI>();
        LoadingUI = services.GetRequiredService<LoadingUI>();
        Clocks = services.Clocks();

        var stateFactory = services.StateFactory();
        var accountSettings = services.GetRequiredService<AccountSettings>();
        _userSettings = stateFactory.NewKvasSynced<UserOnboardingSettings>(
            new (accountSettings, UserOnboardingSettings.KvasKey) {
                InitialValue = new UserOnboardingSettings(),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(UserSettings)),
            });
        var localSettings = services.GetRequiredService<LocalSettings>();
        _localSettings = stateFactory.NewKvasStored<LocalOnboardingSettings>(
            new (localSettings, LocalOnboardingSettings.KvasKey) {
                InitialValue = new LocalOnboardingSettings(),
                Category = StateCategories.Get(GetType(), nameof(LocalSettings)),
            });
    }

    public void Dispose()
    {
        _lastTryShowCts.CancelAndDisposeSilently();
        _userSettings.Dispose();
    }

    public async Task<bool> TryShow()
    {
        // Must start in Blazor Dispatcher!
        if (_lastModalRef is { WhenClosed.IsCompleted: false })
            return true;

        _lastModalRef?.Close(true);
        _lastTryShowCts.CancelAndDisposeSilently();
        var shouldBeShown = false;
        // We give it 5 seconds to complete, otherwise it won't be shown
        using var cts = _lastTryShowCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try {
            shouldBeShown = await ShouldBeShown(cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
        finally {
            if (_lastTryShowCts == cts)
                _lastTryShowCts = null;
            cts.DisposeSilently();
        }
        if (!shouldBeShown)
            return false;

        var modalUI = Services.GetRequiredService<ModalUI>();
        _lastModalRef = await modalUI.Show(new OnboardingModal.Model(), CancellationToken.None).ConfigureAwait(false);
        return true;
    }

    public void UpdateUserSettings(UserOnboardingSettings value)
        => _userSettings.Value = value;

    public void UpdateLocalSettings(LocalOnboardingSettings value)
        => _localSettings.Value = value;

    // Private methods

    private async Task<bool> ShouldBeShown(CancellationToken cancellationToken)
    {
        // Wait for sign-in
        await AccountUI.WhenLoaded.WaitAsync(cancellationToken).ConfigureAwait(false);
        await AccountUI.OwnAccount.When(x => !x.IsGuestOrNone, cancellationToken).ConfigureAwait(false);

        // Wait when settings are read & synchronized
        await _userSettings.WhenFirstTimeRead.ConfigureAwait(false);
        await _userSettings.Synchronize(cancellationToken).ConfigureAwait(false);
        await _localSettings.WhenRead.ConfigureAwait(false);
        await _localSettings.Synchronize(cancellationToken).ConfigureAwait(false);

        // If there was a recent account change, add a delay to let them hit the client
        await Task.Delay(AccountUI.GetPostChangeInvalidationDelay(), cancellationToken).ConfigureAwait(false);

        // Finally, wait for the possibility to render onboarding modal
        await LoadingUI.WhenRendered.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (_userSettings.Value.HasUncompletedSteps)
            return true;

        if (!_localSettings.Value.IsPermissionsStepCompleted) {
            // Fix IsPermissionsStepCompleted based on actual permissions:
            // we don't want to show "Required permissions" screen if they're already granted
            var permissionsStepModel = await PermissionStepModel.New(Services, cancellationToken).ConfigureAwait(false);
            if (permissionsStepModel.SkipEverything) {
                permissionsStepModel.MarkCompleted();
                await Task.Yield(); // Just in case
            }
        }
        return _localSettings.Value.HasUncompletedSteps;
    }
}
