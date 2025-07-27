using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui.Services;

public class MauiAttachmentFilePicker(IServiceProvider services) : IAttachmentFilePicker
{
    private static ILogger Log { get; } = StaticLog.For<MauiAttachmentFilePicker>();

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
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (temp is null)
            return [];

        var filesResults = temp.ToArray();
        if (filesResults.Length == 0)
            return [];

        var fileInfos = new List<AttachFileInfo>();
        var fileRefs = new List<string>();
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
            fileProvider.Initialize(services);
            fileInfos.Add(new AttachFileInfo(fileResult.FileName, fileResult.ContentType, fileLength, fileProvider));
            fileRefs.Add(filePath);
        }
        Log.LogDebug("Picked {Count} files. File refs:\n{FileRefs}",
            fileInfos.Count,
            string.Join(Environment.NewLine, fileRefs.Select(c => "'" + c + "'"))
        );
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
        Log.LogDebug("Picked {Count} visual media files. Uris:\n{Uris}",
            uris.Length,
            string.Join(Environment.NewLine, uris.Select(c => "'" + c + "'"))
        );
        return Downloader.ConvertToAttachFileInfos(uris);
    }
#endif
}
