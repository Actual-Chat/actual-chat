namespace ActualChat.UI.Blazor.Components;

public static class MenuTriggersExt
{
    public static string Format(this MenuTrigger trigger)
        => ((int)trigger).Format();
}
