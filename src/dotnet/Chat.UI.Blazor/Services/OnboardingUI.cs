using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class OnboardingUI : IDisposable, IOnboardingUI
{
    private readonly ISyncedState<UserOnboardingSettings> _settings;
    private CancellationTokenSource? _lastTryShowCts;
    private ModalRef? _lastModalRef;

    private IServiceProvider Services { get; }
    private AccountUI AccountUI { get; }
    private LoadingUI LoadingUI { get; }
    private MomentClockSet Clocks { get; }
    private Moment Now => Clocks.SystemClock.Now;

    public IState<UserOnboardingSettings> Settings => _settings;

    public OnboardingUI(IServiceProvider services)
    {
        Services = services;
        AccountUI = services.GetRequiredService<AccountUI>();
        LoadingUI = services.GetRequiredService<LoadingUI>();
        Clocks = services.Clocks();

        var stateFactory = services.StateFactory();
        var accountSettings = services.GetRequiredService<AccountSettings>();
        _settings = stateFactory.NewKvasSynced<UserOnboardingSettings>(
            new (accountSettings, UserOnboardingSettings.KvasKey) {
                InitialValue = new UserOnboardingSettings(),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
    }

    public void Dispose()
    {
        _lastTryShowCts.CancelAndDisposeSilently();
        _settings.Dispose();
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
            shouldBeShown = await ShouldBeShown(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally {
            if (_lastTryShowCts == cts)
                _lastTryShowCts = null;
            cts.DisposeSilently();
        }
        if (!shouldBeShown)
            return false;

        UpdateSettings(Settings.Value with { LastShownAt = Now });
        var modalUI = Services.GetRequiredService<ModalUI>();
        _lastModalRef = await modalUI.Show(new OnboardingModal.Model()).ConfigureAwait(false);
        return true;
    }

    public void UpdateSettings(UserOnboardingSettings value)
        => _settings.Value = value;

    // Private methods

    private async Task<bool> ShouldBeShown(CancellationToken cancellationToken)
    {
        // Wait for sign-in
        await AccountUI.WhenLoaded.WaitAsync(cancellationToken).ConfigureAwait(false);
        await AccountUI.OwnAccount.When(x => !x.IsGuestOrNone, cancellationToken).ConfigureAwait(false);

        // Wait when settings are read & synchronized
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);
        await _settings.Synchronize(cancellationToken).ConfigureAwait(false);

        // If there was a recent account change, add a delay to let them hit the client
        await Task.Delay(AccountUI.GetPostChangeInvalidationDelay(), cancellationToken).ConfigureAwait(false);

        // Finally, wait for the possibility to render onboarding modal
        await LoadingUI.WhenRendered.WaitAsync(cancellationToken).ConfigureAwait(false);

        var settings = _settings.Value;
        return settings.HasUncompletedSteps;
    }
}
