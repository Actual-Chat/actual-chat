namespace ActualChat.UI.Blazor.Services;

public interface IOnboardingUI : IDisposable
{
    Task<bool> TryShow();
}
