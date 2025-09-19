using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

public class MauiAttachmentFilePicker(IServiceProvider services) : IAttachmentFilePicker
{
    public async Task<AttachFileInfo[]> PickFiles(string acceptTypes)
    {
#if ANDROID
        if (!acceptTypes.IsNullOrEmpty()) {
            var visualMediaFileInfos = await TryPickVisualMediaFiles(acceptTypes).ConfigureAwait(false);
            if (visualMediaFileInfos is not null)
                return visualMediaFileInfos;
        }
#endif

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
            fileInfos.Add(new AttachFileInfo(fileResult.FileName, fileResult.ContentType, fileLength, fileProvider));
        }
        return fileInfos.ToArray();
    }

#if ANDROID
    [field: AllowNull, MaybeNull]
    private VisualMediaFileChooser VisualMediaFileChooser => field ??= new VisualMediaFileChooser(MainActivity.Current);

    [field: AllowNull, MaybeNull]
    private AndroidContentDownloader Downloader => field ??= services.GetRequiredService<AndroidContentDownloader>();

    private async Task<AttachFileInfo[]?> TryPickVisualMediaFiles(string acceptTypes)
    {
        var tcs = TaskCompletionSourceExt.New<Android.Net.Uri[]>();
        if (!VisualMediaFileChooser.OnShowFileChooser(acceptTypes, c => tcs.TrySetResult(c)))
            return null;

        var uris = await tcs.Task.ConfigureAwait(false);
        return Downloader.ConvertToAttachFileInfos(uris);
    }
#endif
}
