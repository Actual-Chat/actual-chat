namespace ActualChat.Chat.UI.Blazor.Components;

public interface IMessageEditorBackend
{
    Task Post(string? text = null);
    Task Cancel();
    Task OpenPrevious();
}
