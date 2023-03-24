namespace ActualChat.UI.Blazor.Components;

public static class BubbleTriggersExt
{
    public static string Format(this BubbleTrigger trigger)
        => ((int)trigger).Format();
}
