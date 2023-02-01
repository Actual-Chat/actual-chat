namespace ActualChat.UI.Blazor.Services;

public sealed class NavigateOnSignInToggle
{
    public bool IsDisabled { get; private set; }

    public void Disable()
        => IsDisabled = true;
}
