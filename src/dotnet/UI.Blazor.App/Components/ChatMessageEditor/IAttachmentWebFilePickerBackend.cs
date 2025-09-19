namespace ActualChat.UI.Blazor.App.Components;

public interface IAttachmentWebFilePickerBackend
{
    Task<bool> OnFilePicked(int id, string? fileName, string? fileType, long length);
}
