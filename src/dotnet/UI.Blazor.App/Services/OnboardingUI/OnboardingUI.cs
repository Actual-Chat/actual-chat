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
    // Offline the decision may never come, and the attention flow waits for it before tips and the review prompt
    private static readonly TimeSpan DecisionTimeout = TimeSpan.FromSeconds(30);

    private AttentionUI AttentionUI => Hub.AttentionUI;
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
        Hub.RegisterDisposable(UserSettings);
    }

    public async Task<bool> ShouldBeShown(CancellationToken cancellationToken)
    {
        using var cts = cancellationToken.CreateLinkedTokenSource(DecisionTimeout);
        try {
            return await ShouldBeShownNow(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            Log.LogWarning(
                "Onboarding is skipped: couldn't decide whether to show it in {Timeout}", DecisionTimeout);
            return false;
        }
    }

    public Task<ModalRef> Show()
    {
        // Onboarding appears on its own (no click to feel), so it plays the open haptic itself -
        // ModalHost no longer does, to avoid doubling it with the tap that opens click-driven modals.
        _ = Hub.TuneUI.Play(Tune.OpenModal);
        return ModalUI.Show(new OnboardingModal.Model());
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

    private async Task<bool> ShouldBeShownNow(CancellationToken cancellationToken)
    {
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

        var userSettings = await GetUserSettingsFromServer(cancellationToken).ConfigureAwait(false);
        if (userSettings.HasUncompletedSteps())
            return true;

        if (userSettings.PasskeyNudgeDeclineCount < MaxPasskeyNudgeDeclineCount
            && await ShouldShowPasskeyStep(cancellationToken).ConfigureAwait(false))
            return true;

        return LocalSettings.Value.HasUncompletedSteps();
    }

    private async Task<UserOnboardingSettings> GetUserSettingsFromServer(CancellationToken cancellationToken)
    {
        // After a reload UserSettings starts from the client cache, which may predate the last write,
        // and its WhenSynchronized doesn't wait for the server - Precise does. UserSettingsUI.Get isn't
        // a compute method (it reads temporals first), so Computed.Capture would get the wrong computed
        var cSettings = await Computed
            .New(Services, UserSettingsUI.UserOnboardingSettings().Get)
            .Update(cancellationToken)
            .ConfigureAwait(false);
        cSettings = await cSettings
            .Synchronize(ComputedSynchronizer.Precise.Instance, cancellationToken)
            .ConfigureAwait(false);
        return cSettings.Value;
    }

    public void ResetSettings()
    {
        UserSettings.Set(new UserOnboardingSettings());
        LocalSettings.Set(new LocalOnboardingSettings());
        _ = ResetPasskeyNudgeSnoozedAt();
    }

    public async Task ResetOnboarding(bool enable)
    {
        // For a skip, suppression closes the modal if it's open and cancels a show the flow already decided on
        // with the old settings; once the flow skips onboarding, it doesn't check it again
        using var _ = enable ? null : AttentionUI.Suppress(AttentionKind.Onboarding);
        // After sign-in the state re-reads the settings for the new account; a read landing after
        // the write below would show the old value until the write's own invalidation comes back
        await Task.Delay(AccountUI.GetPostChangeInvalidationDelay()).ConfigureAwait(false);
        if (enable)
            ResetSettings();
        else
            MarkStepsCompleted();
        // Both writes are deferred, so a navigation right after the reset would otherwise lose them
        await UserSettings.WhenWritten().WaitAsync(TimeSpan.FromSeconds(5)).SilentAwait(false);
        await Hub.LocalSettings.Flush().ConfigureAwait(false);
    }

    private void MarkStepsCompleted()
    {
        UserSettings.Set(UserSettings.Value.WithAllStepsCompleted() with {
            PasskeyNudgeDeclineCount = MaxPasskeyNudgeDeclineCount,
        });
        LocalSettings.Set(LocalSettings.Value.WithAllStepsCompleted());
    }
}
