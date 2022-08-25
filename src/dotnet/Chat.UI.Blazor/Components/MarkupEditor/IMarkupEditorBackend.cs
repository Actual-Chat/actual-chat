namespace ActualChat.Chat.UI.Blazor.Components;

public interface IMarkupEditorBackend
{
    Task Post(string? text = null);
    Task Cancel();
    Task ListCommand(string listId, MarkupEditorListCommand command);
}
