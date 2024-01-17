namespace ActualChat.Chat.UI.Blazor.Components;

public struct SpeedValue(string title, double value)
{
    public string Title => title;
    public double Value => value;
}
