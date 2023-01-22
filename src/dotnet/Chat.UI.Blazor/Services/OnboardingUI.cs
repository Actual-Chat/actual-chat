using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public class OnboardingUI
{
    private ModalUI ModalUI { get; }
    private AccountSettings AccountSettings { get; }

    public ISyncedState<UserOnboardingSettings> Settings { get; }

    public OnboardingUI(IServiceProvider services)
    {
        ModalUI = services.GetRequiredService<ModalUI>();
        AccountSettings = services.GetRequiredService<AccountSettings>();
        var stateFactory = services.StateFactory();
        Settings = stateFactory.NewKvasSynced<UserOnboardingSettings>(
            new (AccountSettings, UserOnboardingSettings.KvasKey) {
                MissingValueFactory = CreateSettings,
                UpdateDelayer = FixedDelayer.Instant,
            });
    }

    public void TryShow()
    {
        if (!Settings.Value.ShouldBeShown())
            return;

        ModalUI.Show(new OnboardingModal.Model());
    }

    private ValueTask<UserOnboardingSettings> CreateSettings(CancellationToken cancellationToken)
        => ValueTask.FromResult(new UserOnboardingSettings());
}
