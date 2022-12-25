using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class OnboardingUI
{
    private ModalUI ModalUI { get; }

    public OnboardingUI(IServiceProvider services)
        => ModalUI = services.GetRequiredService<ModalUI>();

    public void Show()
        => ModalUI.Show(new OnboardingModal.Model());
}
