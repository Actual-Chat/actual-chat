using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

public class MauiAttachmentFilePicker(IServiceProvider services) : IAttachmentFilePicker
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
#if ANDROID
            var javaFile = new Java.IO.File(fileResult.FullPath);
            var androidUri = Android.Net.Uri.FromFile(javaFile)!;
            var filePath = androidUri.ToString()!;
#else
            var filePath = fileResult.FullPath;
#endif
            var fileProvider = new MauiFileProvider {
                FileType = fileResult.ContentType,
                FileName = fileResult.FileName,
                FileRef = filePath,
            };
            var fileInfo = new AttachFileInfo(fileResult.FileName, fileResult.ContentType, fileLength, fileProvider);
            fileInfos.Add(fileInfo);
        }
        return fileInfos.ToArray();
    }
}
