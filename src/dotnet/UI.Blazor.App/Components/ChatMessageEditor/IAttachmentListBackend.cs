namespace ActualChat.UI.Blazor.App.Components;

public interface IAttachmentListBackend
{
    bool OnAttachmentAdded(int id, string url, string? fileName, string? fileType, int length);
    Task OnCreateUploaderRequested(int id, string sChatId);
}
