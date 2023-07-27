using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public sealed class BubbleUI : IDisposable
{
    private readonly ISyncedState<UserBubbleSettings> _settings;
    private AccountUI? _accountUI;

    private IServiceProvider Services { get; }
    private Session Session { get; }
    private AccountUI AccountUI => _accountUI ??= Services.GetRequiredService<AccountUI>();
    private AccountSettings AccountSettings { get; }
    private MomentClockSet Clocks { get; }

    public IState<UserBubbleSettings> Settings => _settings;

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
        await AccountUI.WhenLoaded;
        await Clocks.Timeout(2)
            .ApplyTo(ct => AccountUI.OwnAccount.When(x => !x.IsGuestOrNone, ct), false)
            .ConfigureAwait(false);

        // Wait when settings are read
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);
        await _settings.Synchronize();

        // If there was a recent account change, add a delay to let them hit the client
        await Task.Delay(AccountUI.GetPostChangeInvalidationDelay()).ConfigureAwait(false);
    }

    public void UpdateSettings(UserBubbleSettings value)
        => _settings.Value = value;
}
