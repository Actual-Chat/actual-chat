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
        => _lastTryShowCts.CancelAndDisposeSilently();

    public async Task<bool> TryShow()
    {
        // Must start in Blazor Dispatcher!
        if (_lastModalRef is { WhenClosed.IsCompleted: false })
            return true;

        _lastModalRef?.Close(true);
        _lastTryShowCts.CancelAndDisposeSilently();
        var shouldBeShown = false;
        using var cts = _lastTryShowCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try {
            shouldBeShown = await ShouldBeShown(cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (TimeoutException) { }
        finally {
            if (_lastTryShowCts == cts)
                _lastTryShowCts = null;
            cts.DisposeSilently();
        }
        if (!shouldBeShown)
            return false;

        UpdateSettings(Settings.Value with { LastShownAt = Now });
        var modalUI = Services.GetRequiredService<ModalUI>();
        _lastModalRef = await modalUI.Show(new OnboardingModal.Model());
        return true;
    }

    public void UpdateSettings(UserOnboardingSettings value)
        => _settings.Value = value;

    // Private methods

    private async Task<bool> ShouldBeShown(CancellationToken cancellationToken)
    {
        // 1. Wait for sign-in
        await LoadingUI.WhenRendered.WaitAsync(cancellationToken);
        await AccountUI.WhenLoaded.WaitAsync(cancellationToken);
        await AccountUI.OwnAccount
            .When(x => !x.IsGuestOrNone, Clocks.Timeout(2), cancellationToken)
            .ConfigureAwait(false);

        // 2. Wait when settings are read
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);

        // 3. Extra delay - just in case Origin is somehow set for cached settings
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        await _settings.Synchronize(cancellationToken);

        // 4. Wait when settings migrated
        await _settings.Computed
            .When(x => !x.Origin.IsNullOrEmpty(), Clocks.Timeout(2), cancellationToken)
            .ConfigureAwait(false);

        var settings = _settings.Value;
        return settings.HasUncompletedSteps;
    }
}
