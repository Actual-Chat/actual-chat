using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class MainContextMenuUI
{
    public bool IsOpen { get; set; }

    public MainContextMenuUI()
    {
    }

    public void CloseMenu()
        => IsOpen = false;

    public void MenuToggle()
        => IsOpen = !IsOpen;
}
