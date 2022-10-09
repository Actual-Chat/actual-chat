namespace ActualChat.UI.Blazor.Components;

public static class MenuFormatter
{
    public static string FormatTrigger<TMenu>()
        => typeof(TMenu).Name;
}
