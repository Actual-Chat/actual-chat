using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

public class MauiAttachmentFilePicker : IAttachmentFilePicker
{
    public async Task<AttachFileInfo[]> PickFiles(string acceptTypes)
    {
        var temp = await FilePicker.Default.PickMultipleAsync().ConfigureAwait(true);
        var filesResults = temp.ToArray();
        if (filesResults.Length == 0)
            return [];

        var fileInfos = new List<AttachFileInfo>();
        foreach (var fileResult in filesResults) {
            long fileLength;
            var stream = await fileResult.OpenReadAsync().ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
                fileLength = stream.Length;
            var fileProvider = new LocalFileProvider {
                FilePath = fileResult.FullPath,
                FileType = fileResult.ContentType,
            };
            var fileInfo = new AttachFileInfo(fileResult.FileName, fileResult.ContentType, fileLength, fileProvider);
            fileInfos.Add(fileInfo);
        }
        return fileInfos.ToArray();
    }
}
