namespace ActualChat.Chat.UI.Blazor.Components;

public interface IChatMessageEditorBackend
{
    void CloseNotifyPanel();
    bool OnAttachmentAdded(int id, string url, string? fileName, string? fileType, int length);
    void OnUploadProgress(int id, int progress);
}
