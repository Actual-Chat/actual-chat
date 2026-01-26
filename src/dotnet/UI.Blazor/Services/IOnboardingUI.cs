namespace ActualChat.UI.Blazor.Services;

public interface IOnboardingUI
{
    Task<bool> TryShow();

    /// <summary>
    /// Resets onboarding state.
    /// </summary>
    /// <param name="enable">
    /// If true, resets all steps to uncompleted (re-enables onboarding).
    /// If false, marks all steps as completed (skips onboarding).
    /// </param>
    void ResetOnboarding(bool enable);
}
