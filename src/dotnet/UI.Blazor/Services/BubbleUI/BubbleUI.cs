using ActualChat.Kvas;
using ActualChat.UI.Blazor.Components;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Manages onboarding bubble tooltips with user-specific read/unread state.
/// </summary>
public sealed class BubbleUI : UIServiceBase<UIHub>
{
    private readonly SyncedState<UserBubbleSettings> _settings;

    public IState<UserBubbleSettings> Settings => _settings;
    public TaskCompletionSource<BubbleHost> HostAcceptor { get; } = TaskCompletionSourceExt.New<BubbleHost>();
    public Task WhenReady => HostAcceptor.Task;
    public BubbleHost Host => field ??= HostAcceptor.Task.RequireResult();

    public BubbleUI(UIHub hub) : base(hub)
    {
        _settings = StateFactory.NewAccountSettingsSynced<UserBubbleSettings>(
            AccountSettings,
            UserBubbleSettings.KvasKey,
            new UserBubbleSettings(),
            updateDelayer: FixedDelayer.NextTick,
            category: StateCategories.Get(GetType(), nameof(Settings)));
        Hub.RegisterDisposable(_settings);
    }

    public async Task WhenReadyToShowBubbles()
    {
        // Wait for sign-in
        await AccountUI.WhenReady.ConfigureAwait(false);
        await Clocks.Timeout(2)
            .ApplyTo(ct => AccountUI.OwnAccount.Computed.When(x => !x.IsGuestOrNull(), ct))
            .SilentAwait(false);
        // If there was a recent account change, add a delay to let invalidations propagate
        await Task.Delay(AccountUI.GetPostChangeInvalidationDelay()).ConfigureAwait(false);

        // Re-synchronize after the invalidation delay to pick up user-specific data
        await _settings.WhenSynchronized().ConfigureAwait(false);

        // Delay first display to not interfere with permissions
        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
    }

    public void UpdateSettings(UserBubbleSettings value)
        => _settings.Value = value;

    public async Task UnreadBubble<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TBubble>()
        where TBubble : IBubble
    {
        var bubbleRef = BubbleRegistry.GetTypeId(typeof(TBubble));
        var updated = Settings.Value.WithoutRead(bubbleRef);
        if (ReferenceEquals(updated, Settings.Value))
            return;
        UpdateSettings(updated);
        await Host.ResetBubbles(updated.ReadBubbles).ConfigureAwait(false);
    }

    public async Task ResetSettings() {
        await WhenReady.ConfigureAwait(true);
        UpdateSettings(Settings.Value.WithAllUnread());
        await Host.ResetBubbles().ConfigureAwait(false);
    }

    /// <summary>
    /// Resets bubble state.
    /// </summary>
    /// <param name="enable">
    /// If true, resets all bubbles to unread (re-enables all bubbles).
    /// If false, marks all bubbles as read (skips all bubbles).
    /// </param>
    public async Task ResetBubbles(bool enable) {
        await WhenReady.ConfigureAwait(true);
        if (enable) {
            UpdateSettings(Settings.Value.WithAllUnread());
            await Host.ResetBubbles().ConfigureAwait(false);
        }
        else {
            // Skip all bubbles by calling SkipBubbles on the host
            await Host.SkipBubbles().ConfigureAwait(false);
        }
    }
}
