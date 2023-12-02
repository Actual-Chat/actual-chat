using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public sealed class BubbleUI : ScopedServiceBase, IHasAcceptor<BubbleHost>
{
    private readonly Acceptor<BubbleHost> _hostAcceptor = new(true);
    private readonly ISyncedState<UserBubbleSettings> _settings;
    private AccountUI? _accountUI;

    private AccountUI AccountUI => _accountUI ??= Services.GetRequiredService<AccountUI>();
    private AccountSettings AccountSettings { get; }
    Acceptor<BubbleHost> IHasAcceptor<BubbleHost>.Acceptor => _hostAcceptor;

    public IState<UserBubbleSettings> Settings => _settings;
    public Task WhenReady => _hostAcceptor.WhenAccepted();
    public BubbleHost Host => _hostAcceptor.Value;

    public BubbleUI(IServiceProvider services) : base(services)
    {
        AccountSettings = services.GetRequiredService<AccountSettings>();
        _settings = StateFactory.NewKvasSynced<UserBubbleSettings>(
            new (AccountSettings, UserBubbleSettings.KvasKey) {
                InitialValue = new UserBubbleSettings(),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
        Scope.RegisterDisposable(_settings);
    }

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
