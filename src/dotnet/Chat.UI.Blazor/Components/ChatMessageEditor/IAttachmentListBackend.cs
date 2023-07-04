namespace ActualChat.Chat.UI.Blazor.Components;

public interface IAttachmentListBackend
{
    bool OnAttachmentAdded(int id, string url, string? fileName, string? fileType, int length);
    void OnUploadProgress(int id, int progress);
    void OnUploadSucceed(int id, MediaId mediaId);
}
