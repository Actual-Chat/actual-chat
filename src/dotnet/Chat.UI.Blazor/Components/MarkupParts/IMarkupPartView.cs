namespace ActualChat.Chat.UI.Blazor.Components.MarkupParts;

public interface IMarkupPartView
{
    ChatEntry Entry { get; set; }
    MarkupPart Part { get; set; }
}
