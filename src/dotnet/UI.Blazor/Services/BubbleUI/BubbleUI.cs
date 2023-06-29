using ActualChat.Kvas;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public sealed class BubbleUI
{
    private readonly ISyncedState<UserBubbleSettings> _settings;

    public IState<UserBubbleSettings> Settings => _settings;

    public BubbleUI(IServiceProvider services)
    {
        var stateFactory = services.StateFactory();
        var accountSettings = services.GetRequiredService<AccountSettings>();
        _settings = stateFactory.NewKvasSynced<UserBubbleSettings>(
            new (accountSettings, UserBubbleSettings.KvasKey) {
                InitialValue = new UserBubbleSettings(),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
    }

    public Task WhenReady => _settings.WhenFirstTimeRead;

    public void UpdateSettings(UserBubbleSettings value)
        => _settings.Value = value;
}
