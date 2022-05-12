using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public sealed class MainContextMenuUI
{
    public bool IsMenuOpened { get; set; }

    public MainContextMenuUI(IServiceProvider serviceProvider)
    {
    }

    public string GetButtonId()
    {
        return "";
    }

    public string GetMenuId()
    {
        return "";
    }

    public void OpenContextMenu()
        => IsMenuOpened = !IsMenuOpened;
}
