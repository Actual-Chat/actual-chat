namespace ActualChat.UI.Blazor.Services;

// AttentionUI decides when onboarding runs; this is what it runs
public interface IOnboardingUI
{
    Task<bool> ShouldBeShown(CancellationToken cancellationToken);
    Task<ModalRef> Show();

    /// <summary>
    /// Debug and test aid for onboarding.
    /// </summary>
    /// <param name="enable">
    /// If true, brings onboarding back with every step uncompleted.
    /// If false, marks every step completed.
    /// </param>
    Task ResetOnboarding(bool enable);
}
