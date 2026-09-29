using ActualChat.Kvas;

namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Manages onboarding bubble tooltips with user-specific read/unread state.
/// </summary>
public sealed class BubbleUI : UIServiceBase<UIHub>
{
    public SyncedState<UserBubbleSettings> Settings { get; init; }
    public TaskCompletionSource<BubbleHost> HostAcceptor { get; } = TaskCompletionSourceExt.New<BubbleHost>();
    public Task WhenReady => field ??= WhenHostJSReady();
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
    /// Debug and test aid for bubbles.
    /// </summary>
    /// <param name="enable">
    /// If true, brings all bubbles back unread.
    /// If false, marks all bubbles read.
    /// </param>
    public async Task ResetBubbles(bool enable)
    {
        // After sign-in the state re-reads the settings for the new account; a read landing after
        // the write below would show the old value until the write's own invalidation comes back.
        // The host calls below render, so they must stay on the Blazor dispatcher
        await Task.Delay(AccountUI.GetPostChangeInvalidationDelay()).ConfigureAwait(true);
        if (enable)
            await ResetSettings().ConfigureAwait(false);
        else
            await MarkAllRead().ConfigureAwait(false);
        // The write is deferred, so a navigation right after the reset would otherwise lose it
        await Settings.WhenWritten().WaitAsync(TimeSpan.FromSeconds(5)).SilentAwait(false);
    }

    // Private methods

    private async Task MarkAllRead()
    {
        // The host knows only the bubbles on the page, so the ones rendered later would show up after it
        UpdateSettings(Settings.Value.WithRead(BubbleRegistry.GetAllTypeIds()));
        await WhenReady.ConfigureAwait(true);
        await Host.SkipBubbles().ConfigureAwait(false);
    }

    private async Task WhenHostJSReady()
    {
        // The host is accepted on init, but its JS object is created after the first render;
        // every Host call before that throws on a null JS reference
        var host = await HostAcceptor.Task.ConfigureAwait(false);
        await host.WhenJSReady.ConfigureAwait(false);
    }
}
