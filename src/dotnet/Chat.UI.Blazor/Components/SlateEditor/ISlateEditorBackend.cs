namespace ActualChat.Chat.UI.Blazor.Components;

public interface ISlateEditorBackend
{
    Task OnPost(string? text = null);
    Task OnCancel();
    Task OnOpenPrevious();
    Task OnMentionCommand(string cmd, string args);
}
