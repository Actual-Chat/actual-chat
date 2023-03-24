namespace ActualChat.UI.Blazor.Components;

public interface IBubble : IHasId<string>
{
    string[] Arguments { get; set; }
    BubbleHost Host { get; set; }
}
