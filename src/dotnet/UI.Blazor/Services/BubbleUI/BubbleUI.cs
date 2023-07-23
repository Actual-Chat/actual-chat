using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public sealed class BubbleUI
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

    public async Task WhenReadyToRenderHost()
    {
        // 1. Wait for sign-in
        try {
            await AccountUI.WhenLoaded;
            await AccountUI.OwnAccount
                .When(x => !x.IsGuestOrNone, Clocks.Timeout(2))
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            // Intended
        }

        // 2. Wait when settings are read
        await _settings.WhenFirstTimeRead.ConfigureAwait(false);

        // 3. Extra delay - just in case Origin is somehow set for cached settings
        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        // 4.Wait when settings migrated
        try {
            await _settings.Computed
                .When(x => !x.Origin.IsNullOrEmpty(), Clocks.Timeout(1))
                .ConfigureAwait(false);
        }
        catch (TimeoutException) {
            // Intended
        }
    }

    public void UpdateSettings(UserBubbleSettings value)
        => _settings.Value = value;
}
