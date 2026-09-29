namespace ActualChat.UI.Blazor.Services;

// AttentionUI decides when onboarding runs; this is what it runs
public interface IOnboardingUI
{
    Task<bool> ShouldBeShown(CancellationToken cancellationToken);
    Task<ModalRef> Show();

    /// <summary>
    /// Resets onboarding state.
    /// </summary>
    /// <param name="enable">
    /// If true, resets all steps to uncompleted (re-enables onboarding).
    /// If false, marks all steps as completed (skips onboarding).
    /// </param>
    void ResetOnboarding(bool enable);
}
