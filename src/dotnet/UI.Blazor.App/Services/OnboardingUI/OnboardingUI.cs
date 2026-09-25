using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Manages user onboarding flow with step-by-step settings and modal display.
/// </summary>
public class OnboardingUI : UIServiceBase<AppUIHub>, IOnboardingUI
{
    private const int MaxPasskeyNudgeDeclineCount = 3;
    private static readonly TimeSpan PasskeyNudgeInterval = TimeSpan.FromDays(7);
    private static readonly SemaphoreSlim Lock = new (1);
    private CancellationTokenSource? _lastTryShowCts;
    private ModalRef? _lastModalRef;

    private LoadingUI LoadingUI => Hub.LoadingUI;
    private PasskeyUI PasskeyUI => Hub.PasskeyUI;
    private LocalStorage LocalStorage => Hub.LocalStorage;

    public SyncedState<UserOnboardingSettings> UserSettings { get; init; }
    public new StoredState<LocalOnboardingSettings> LocalSettings { get; init; }
    public Task WhenLocalSettingsRead => LocalSettings.WhenRead;

    public OnboardingUI(AppUIHub hub) : base(hub)
    {
        var stateFactory = hub.StateFactory;
        var localSettings = hub.LocalSettings;
        var type = GetType();
        UserSettings = stateFactory.NewUserSettingsSynced(
            UserSettingsUI,
            UserOnboardingSettings.KvasKey,
            new UserOnboardingSettings(),
            updateDelayer: FixedDelayer.NextTick,
            category: StateCategories.Get(type, nameof(UserSettings)));
        LocalSettings = stateFactory.NewKvasStored<LocalOnboardingSettings>(
            new (localSettings, LocalOnboardingSettings.KvasKey) {
                InitialValue = new LocalOnboardingSettings(),
                Category = StateCategories.Get(type, nameof(LocalSettings)),
            });
        Hub.RegisterDisposable(() => {
            _lastTryShowCts.CancelAndDisposeSilently();
            UserSettings.Dispose();
        });
    }

    public async Task<bool> TryShow()
    {
        await Lock.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try {
            // Must start in Blazor Dispatcher!
            if (_lastModalRef is { WhenClosed.IsCompleted: false })
                return true;

            _lastModalRef?.Close(true);
            _lastTryShowCts.CancelAndDisposeSilently();
            var shouldBeShown = false;
            // We give it 5 seconds to complete, otherwise it won't be shown
            using var cts = _lastTryShowCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try {
                shouldBeShown = await ShouldBeShown(cts.Token)
                    .ConfigureAwait(true); // true is required here!
            }
            catch (OperationCanceledException) { }
            finally {
                if (_lastTryShowCts == cts)
                    _lastTryShowCts = null;
                cts.DisposeSilently();
            }
            if (!shouldBeShown)
                return false;

            _lastModalRef = await ModalUI
                .Show(new OnboardingModal.Model(), CancellationToken.None)
                .ConfigureAwait(false); // Ok (pre-exit)
            return true;
        }
        finally {
            Lock.Release();
        }
    }

    public void UpdateUserSettings(UserOnboardingSettings value)
        => UserSettings.Set(value);

    public void UpdateLocalSettings(LocalOnboardingSettings value)
        => LocalSettings.Set(value);

    // Not a compute method: OnboardingUI is a plain scoped service (services.AddScoped in
    // BlazorUIAppModule), and the answer is only needed at the moment the modal opens
    public async Task<bool> ShouldShowPasskeyStep(CancellationToken cancellationToken)
    {
        if (!await PasskeyUI.CanUse(cancellationToken).ConfigureAwait(false))
            return false;

        var passkeys = await PasskeyUI.ListOwn(cancellationToken).ConfigureAwait(false);
        if (passkeys.Count > 0)
            return false;

        await UserSettings.WhenSynchronized(ComputedSynchronizer.Current, cancellationToken).ConfigureAwait(false);
        if (UserSettings.Value.PasskeyNudgeDeclineCount >= MaxPasskeyNudgeDeclineCount)
            return false;

        var lastSnoozedAt = await GetPasskeyNudgeSnoozedAt(cancellationToken).ConfigureAwait(false);
        return Clocks.SystemClock.Now - lastSnoozedAt > PasskeyNudgeInterval;
    }

    public async Task SnoozePasskeyStep()
    {
        // The decline count is per account, so the last one ends the nudge on every device;
        // the pause between them is per device, in LocalStorage, which sign-out doesn't wipe
        var settings = UserSettings.Value;
        UpdateUserSettings(settings with { PasskeyNudgeDeclineCount = settings.PasskeyNudgeDeclineCount + 1 });
        var now = Clocks.SystemClock.Now.EpochOffsetTicks.ToString();
        await LocalStorage.SetString(GetPasskeyNudgeSnoozedAtKey(), now).SilentAwait();
    }

    // Private methods

    private async Task<Moment> GetPasskeyNudgeSnoozedAt(CancellationToken cancellationToken)
    {
        try {
            var value = await LocalStorage.GetString(GetPasskeyNudgeSnoozedAtKey()).ConfigureAwait(false);
            return long.TryParse(value, out var ticks)
                ? new Moment(ticks)
                : default;
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            // An unreadable pause counts as a fresh one: skipping a nudge is cheaper than nagging
            Log.LogWarning(e, "Failed to read the passkey nudge pause");
            return Moment.MaxValue;
        }
    }

    private async Task ResetPasskeyNudgeSnoozedAt()
        => await LocalStorage.RemoveItem(GetPasskeyNudgeSnoozedAtKey()).SilentAwait();

    private string GetPasskeyNudgeSnoozedAtKey()
        => $"{nameof(OnboardingUI)}.PasskeyNudgeSnoozedAt.{AccountUI.OwnAccount.Value.Id}";

    private async Task<bool> ShouldBeShown(CancellationToken cancellationToken)
    {
        // Wait for sign-in
        await AccountUI.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        await AccountUI.OwnAccount.Computed
            .When(x => !x.IsGuest, cancellationToken)
            .ConfigureAwait(false);
        // If there was a recent account change, add a delay to let them hit the client
        await Task.Delay(AccountUI.GetPostChangeInvalidationDelay(), cancellationToken).ConfigureAwait(false);

        // Wait when settings are read & synchronized
        await UserSettings.WhenSynchronized(ComputedSynchronizer.Current, cancellationToken).ConfigureAwait(false);
        await LocalSettings.WhenSynchronized(ComputedSynchronizer.Current, cancellationToken).ConfigureAwait(false);

        // Finally, wait for the possibility to render onboarding modal
        await LoadingUI.WhenRendered.WaitAsync(cancellationToken).ConfigureAwait(false);

        if (!LocalSettings.Value.IsPermissionsStepCompleted) {
            // Fix IsPermissionsStepCompleted based on actual permissions before anything else opens the modal:
            // we don't want to show the "Required permissions" screen if they're already granted
            var permissionsStepModel = await PermissionStepModel.New(Services, cancellationToken).ConfigureAwait(false);
            if (permissionsStepModel.SkipEverything) {
                permissionsStepModel.MarkCompleted();
                await Task.Yield(); // Just in case
            }
        }

        if (UserSettings.Value.HasUncompletedSteps())
            return true;

        if (await ShouldShowPasskeyStep(cancellationToken).ConfigureAwait(false))
            return true;

        return LocalSettings.Value.HasUncompletedSteps();
    }

    public void ResetSettings()
    {
        UserSettings.Set(new UserOnboardingSettings());
        LocalSettings.Set(new LocalOnboardingSettings());
        _ = ResetPasskeyNudgeSnoozedAt();
    }

    public void ResetOnboarding(bool enable)
    {
        if (enable) {
            // Reset all steps to uncompleted (re-enable onboarding)
            UserSettings.Set(new UserOnboardingSettings());
            LocalSettings.Set(new LocalOnboardingSettings());
            _ = ResetPasskeyNudgeSnoozedAt();
        }
        else {
            // Mark all steps as completed (skip onboarding)
            UserSettings.Set(new UserOnboardingSettings {
                IsAvatarStepCompleted = true,
                // IsCreateChatsStepCompleted = true, // Disabled
                IsVerifyPhoneStepCompleted = true,
                IsVerifyEmailStepCompleted = true,
                // IsTimeZoneStepCompleted = true, // Disabled
                IsDataCollectionStepCompleted = true,
                IsTranscriptionTutorialStepCompleted = true,
                // IsTranscriptReplayTutorialStepCompleted = true, // Disabled
                IsPlacesTutorialStepCompleted = true,
                IsLanguagesStepCompleted = true,
                IsSummarizationTutorialStepCompleted = true,
                PasskeyNudgeDeclineCount = MaxPasskeyNudgeDeclineCount,
            });
            LocalSettings.Set(new LocalOnboardingSettings {
                IsPermissionsStepCompleted = true,
                AreCookiesAccepted = true,
            });
            // Close the onboarding modal if it's open
            _lastModalRef?.Close(true);
        }
    }
}
