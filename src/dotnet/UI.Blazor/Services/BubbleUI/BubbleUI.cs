using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public sealed class BubbleUI
{
    private readonly ISyncedState<UserBubblesSettings> _settings;

    public IState<UserBubblesSettings> Settings => _settings;

    public BubbleUI(IServiceProvider services)
    {
        var stateFactory = services.StateFactory();
        var accountSettings = services.GetRequiredService<AccountSettings>();
        _settings = stateFactory.NewKvasSynced<UserBubblesSettings>(
            new (accountSettings, UserBubblesSettings.KvasKey) {
                InitialValue = new UserBubblesSettings(),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
    }

    public Task WhenReady => _settings.WhenFirstTimeRead;

    public void UpdateSettings(UserBubblesSettings value)
        => _settings.Value = value;
}
