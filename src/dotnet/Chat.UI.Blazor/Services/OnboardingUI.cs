using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class OnboardingUI
{
    private Session Session { get; }
    private IAccounts Accounts { get; }
    private ModalUI ModalUI { get; }
    private AccountSettings AccountSettings { get; }

    public ISyncedState<UserOnboardingSettings> Settings { get; }

    public OnboardingUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        ModalUI = services.GetRequiredService<ModalUI>();
        AccountSettings = services.GetRequiredService<AccountSettings>();
        var stateFactory = services.StateFactory();
        Settings = stateFactory.NewKvasSynced<UserOnboardingSettings>(
            new (AccountSettings, UserOnboardingSettings.KvasKey) {
                InitialValue = new UserOnboardingSettings(),
                UpdateDelayer = FixedDelayer.Instant,
            });
    }

    public async Task TryShow()
    {
        var account = await Accounts.GetOwn(Session, CancellationToken.None);
        if (account.IsGuest)
            return;

        await Settings.WhenFirstTimeRead;
        if (!Settings.Value.ShouldBeShown())
            return;

        Settings.Value = Settings.Value with {
            LastShownAt = DateTime.UtcNow,
        };
        await ModalUI.Show(new OnboardingModal.Model());
    }
}
