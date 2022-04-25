namespace ActualChat.Chat.UI.Blazor.Components;

public interface IChatMessageEditorBackend
{
    bool AddAttachment(int id, string url, string? fileName, string? fileType, int length);
}
