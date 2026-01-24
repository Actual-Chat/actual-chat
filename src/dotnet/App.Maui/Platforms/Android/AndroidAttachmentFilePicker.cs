using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Components;
using ActualLab.IO;

namespace ActualChat.App.Maui;

public class AndroidAttachmentFilePicker(IServiceProvider services) : MauiAttachmentFilePicker(services)
{
    private VisualMediaFileChooser VisualMediaFileChooser => field ??= new VisualMediaFileChooser(MainActivity.Current);

    private AndroidContentDownloader Downloader => field ??= Services.GetRequiredService<AndroidContentDownloader>();

    protected override FilePath GetFullPath(FileResult fileResult)
    {
        var javaFile = new Java.IO.File(fileResult.FullPath);
        var androidUri = Android.Net.Uri.FromFile(javaFile)!;
        return androidUri.ToString()!;
    }

    protected override async Task<AttachFileInfo[]?> TryPickVisualMediaFiles(string acceptTypes)
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
}
