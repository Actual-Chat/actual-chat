using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public record AttachFileInfo(IFileProvider FileProvider);

public interface IAttachmentFilePicker
{
    Task<AttachFileInfo[]> PickFiles(string acceptTypes);
}
