namespace ActualChat.UI.Blazor.Components;

public static class MenuTriggersExt
{
    public static string Format(this MenuTriggers triggers)
        => ((int)triggers).Format();
}
