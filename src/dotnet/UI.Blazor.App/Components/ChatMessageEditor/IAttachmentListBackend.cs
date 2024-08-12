namespace ActualChat.UI.Blazor.App.Components;

public interface IAttachmentListBackend
{
    bool OnAttachmentAdded(int id, string url, string? fileName, string? fileType, int length);
    void OnUploadProgress(int id, int progress);
    void OnUploadSucceed(int id, MediaId mediaId, MediaId thumbnailMediaId);
    void OnUploadFailed(int id);
}
