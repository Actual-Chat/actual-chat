namespace ActualChat.UI.Blazor.Components;

public interface IBubble : IHasId<string>
{
    BubbleHost Host { get; }
    public int Count { get; }
    public int Current { get; }
}
