using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public sealed class BubbleUI : IDisposable, IHasAcceptor<BubbleHost>
{
    private readonly Acceptor<BubbleHost> _hostAcceptor = new(true);
    private readonly ISyncedState<UserBubbleSettings> _settings;
    private AccountUI? _accountUI;

    private IServiceProvider Services { get; }
    private Session Session { get; }
    private AccountUI AccountUI => _accountUI ??= Services.GetRequiredService<AccountUI>();
    private AccountSettings AccountSettings { get; }
    private MomentClockSet Clocks { get; }
    Acceptor<BubbleHost> IHasAcceptor<BubbleHost>.Acceptor => _hostAcceptor;

    public IState<UserBubbleSettings> Settings => _settings;
    public Task WhenReady => _hostAcceptor.WhenAccepted();
    public BubbleHost Host => _hostAcceptor.Value;

    public BubbleUI(IServiceProvider services)
    {
        Services = services;
        Session = services.Session();
        AccountSettings = services.GetRequiredService<AccountSettings>();
        Clocks = services.Clocks();

        var stateFactory = services.StateFactory();
        _settings = stateFactory.NewKvasSynced<UserBubbleSettings>(
            new (AccountSettings, UserBubbleSettings.KvasKey) {
                InitialValue = new UserBubbleSettings(),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
    }

    public void Dispose()
        => _settings.Dispose();

    public async Task WhenReadyToShowBubbles()
    {
        // Wait for sign-in
        await AccountUI.WhenLoaded.ConfigureAwait(false);
        await Clocks.Timeout(2)
            .ApplyTo(ct => AccountUI.OwnAccount.When(x => !x.IsGuestOrNone, ct))
            .SilentAwait(false);

        // Wait when settings are read
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);
        await _settings.Synchronize().ConfigureAwait(false);

        // If there was a recent account change, add a delay to let them hit the client
        await Task.Delay(AccountUI.GetPostChangeInvalidationDelay()).ConfigureAwait(false);
    }

    public void UpdateSettings(UserBubbleSettings value)
        => _settings.Value = value;

    public async Task ResetSettings() {
        await WhenReady.ConfigureAwait(true);
        UpdateSettings(Settings.Value.WithAllUnread());
        await Host.ResetBubbles().ConfigureAwait(false);
    }
}
