using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Manages onboarding bubble tooltips with user-specific read/unread state.
/// </summary>
public sealed class BubbleUI : UIServiceBase<UIHub>
{
    public SyncedState<UserBubbleSettings> Settings { get; init; }
    public TaskCompletionSource<BubbleHost> HostAcceptor { get; } = TaskCompletionSourceExt.New<BubbleHost>();
    public Task WhenReady => HostAcceptor.Task;
    public BubbleHost Host => field ??= HostAcceptor.Task.RequireResult();

    public BubbleUI(UIHub hub) : base(hub)
    {
        Settings = StateFactory.NewUserSettingsSynced(
            UserSettingsUI,
            UserBubbleSettings.KvasKey,
            new UserBubbleSettings(),
            updateDelayer: FixedDelayer.NextTick,
            category: StateCategories.Get(GetType(), nameof(Settings)));
        Hub.RegisterDisposable(Settings);
    }

    public async Task<IReadOnlyList<Symbol>> GetReadBubbles(CancellationToken cancellationToken)
    {
        // The current account's, once its settings reached the client:
        // after a recent account change, invalidations need a moment to propagate
        await Task.Delay(AccountUI.GetPostChangeInvalidationDelay(), cancellationToken).ConfigureAwait(false);
        await Settings.WhenSynchronized(cancellationToken).ConfigureAwait(false);
        return Settings.Value.ReadBubbles;
    }

    public void UpdateSettings(UserBubbleSettings value)
        => Settings.Set(value);

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
