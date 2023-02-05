using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class OnboardingUI
{
    private Session Session { get; }
    private IAccounts Accounts { get; }
    private ModalUI ModalUI { get; }

    public ISyncedState<UserOnboardingSettings> Settings { get; }

    public OnboardingUI(IServiceProvider services)
    {
        Session = services.GetRequiredService<Session>();
        Accounts = services.GetRequiredService<IAccounts>();
        ModalUI = services.GetRequiredService<ModalUI>();

        var stateFactory = services.StateFactory();
        var accountSettings = services.GetRequiredService<AccountSettings>();
        Settings = stateFactory.NewKvasSynced<UserOnboardingSettings>(
            new (accountSettings, UserOnboardingSettings.KvasKey) {
                InitialValue = new UserOnboardingSettings(),
                UpdateDelayer = FixedDelayer.Instant,
                Category = StateCategories.Get(GetType(), nameof(Settings)),
            });
    }

    public async Task TryShow()
    {
        var account = await Accounts.GetOwn(Session, CancellationToken.None);
        if (account.IsGuestOrNone)
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
