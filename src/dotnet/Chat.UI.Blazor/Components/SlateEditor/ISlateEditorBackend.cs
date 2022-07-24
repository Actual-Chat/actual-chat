namespace ActualChat.Chat.UI.Blazor.Components;

public interface ISlateEditorBackend
{
    Task Post(string? text = null);
    Task Cancel();
    Task EditLastMessage();
}
