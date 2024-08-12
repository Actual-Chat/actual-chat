namespace ActualChat.UI.Blazor.App.Components;

public interface IMarkupEditorBackend
{
    Task OnPost(string? text = null);
    Task OnCancel();
    Task OnOpenPrevious();
    Task OnListCommand(string listId, MarkupEditorListCommand command);
}
