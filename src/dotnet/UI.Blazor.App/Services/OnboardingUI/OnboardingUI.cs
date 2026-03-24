using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Manages user onboarding flow with step-by-step settings and modal display.
/// </summary>
public class OnboardingUI : UIServiceBase<AppUIHub>, IOnboardingUI
{
    private static readonly SemaphoreSlim Lock = new (1);
    private CancellationTokenSource? _lastTryShowCts;
    private ModalRef? _lastModalRef;

    private LoadingUI LoadingUI => Hub.LoadingUI;

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

    // Private methods

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

        if (UserSettings.Value.HasUncompletedSteps())
            return true;

        if (!LocalSettings.Value.IsPermissionsStepCompleted) {
            // Fix IsPermissionsStepCompleted based on actual permissions:
            // we don't want to show the "Required permissions" screen if they're already granted
            var permissionsStepModel = await PermissionStepModel.New(Services, cancellationToken).ConfigureAwait(false);
            if (permissionsStepModel.SkipEverything) {
                permissionsStepModel.MarkCompleted();
                await Task.Yield(); // Just in case
            }
        }
        return LocalSettings.Value.HasUncompletedSteps();
    }

    public void ResetSettings()
    {
        UserSettings.Set(new UserOnboardingSettings());
        LocalSettings.Set(new LocalOnboardingSettings());
    }

    public void ResetOnboarding(bool enable)
    {
        if (enable) {
            // Reset all steps to uncompleted (re-enable onboarding)
            UserSettings.Set(new UserOnboardingSettings());
            LocalSettings.Set(new LocalOnboardingSettings());
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
