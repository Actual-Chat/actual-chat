namespace ActualChat.UI.Blazor.App.Components;

public interface IAttachmentWebFilePickerBackend
{
    Task OnFilePicked(WebFileInfo[] fileInfos);
}

public record WebFileInfo(int Id, string FileName, string FileType, long Size);
