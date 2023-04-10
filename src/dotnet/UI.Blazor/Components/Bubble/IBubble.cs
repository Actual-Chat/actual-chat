namespace ActualChat.UI.Blazor.Components;

public interface IBubble : IHasId<string>
{
    BubbleHost Host { get; set; }
}
