using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public sealed class BubbleUI : UIServiceBase<UIHub>
{
    private readonly SyncedState<UserBubbleSettings> _settings;

    public IState<UserBubbleSettings> Settings => _settings;
    public TaskCompletionSource<BubbleHost> HostAcceptor { get; } = TaskCompletionSourceExt.New<BubbleHost>();
    public Task WhenReady => HostAcceptor.Task;
    public BubbleHost Host => field ??= HostAcceptor.Task.RequireResult();

    public BubbleUI(UIHub hub) : base(hub)
    {
        _settings = StateFactory.NewKvasSynced<UserBubbleSettings>(
            new (AccountSettings, UserBubbleSettings.KvasKey) {
                InitialValue = new UserBubbleSettings(),
                UpdateDelayer = FixedDelayer.NextTick,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
        Hub.RegisterDisposable(_settings);
    }

    public async Task WhenReadyToShowBubbles()
    {
        // Wait for sign-in
        await AccountUI.WhenReady.ConfigureAwait(false);
        await Clocks.Timeout(2)
            .ApplyTo(ct => AccountUI.OwnAccount.Computed.When(x => !x.IsGuestOrNull(), ct))
            .SilentAwait(false);

        // Wait when settings are read
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);
        await _settings.Computed.Synchronize().ConfigureAwait(false);

        // Delay first display to not interfere with permissions
        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
    }

    public void UpdateSettings(UserBubbleSettings value)
        => _settings.Value = value;

    public async Task ResetSettings() {
        await WhenReady.ConfigureAwait(true);
        UpdateSettings(Settings.Value.WithAllUnread());
        await Host.ResetBubbles().ConfigureAwait(false);
    }
}
