namespace ActualChat.UI.Blazor.Components;

public static class MenuTriggerFormatter
{
    public static string Format<TMenu>()
        => typeof(TMenu).Name;
}
