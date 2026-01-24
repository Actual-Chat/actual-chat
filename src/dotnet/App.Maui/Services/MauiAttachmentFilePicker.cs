using ActualChat.Media;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui.Services;

public class MauiAttachmentFilePicker(IServiceProvider services) : IAttachmentFilePicker
{
    protected IServiceProvider Services => services;
    protected ILogger Log => field ??= Services.LogFor(GetType());

    public async Task<AttachFileInfo[]> PickFiles(string acceptTypes)
    {
        if (MediaTypeExt.IsVisualMedia(acceptTypes)) {
            var visualMediaFileInfos = await TryPickVisualMediaFiles(acceptTypes).ConfigureAwait(false);
            if (visualMediaFileInfos is not null)
                return visualMediaFileInfos;
        }

        var temp = await FilePicker.Default.PickMultipleAsync().ConfigureAwait(true);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (temp is null)
            return [];

        var filesResults = temp.ToArray();
        if (filesResults.Length == 0)
            return [];

        var fileInfos = new List<AttachFileInfo>();
        var fileRefs = new List<FilePath>();
        foreach (var fileResult in filesResults) {
            long fileLength;
            var stream = await fileResult.OpenReadAsync().ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
                fileLength = stream.Length;
            var filePath = GetFullPath(fileResult);
            var fileProvider = new MauiFileProvider {
                Metadata = new () {
                    FileName = fileResult.FileName,
                    FileType = fileResult.ContentType,
                    Length = fileLength,
                },
                FileRef = filePath,
            };
            fileProvider.Initialize(services);
            fileInfos.Add(new AttachFileInfo(fileProvider));
            fileRefs.Add(filePath);
        }
        Log.LogDebug("Picked {Count} files. File refs:\n{FileRefs}",
            fileInfos.Count,
            string.Join(Environment.NewLine, fileRefs.Select(c => "'" + c + "'"))
        );
        return fileInfos.ToArray();
    }

    protected virtual FilePath GetFullPath(FileResult fileResult)
        => fileResult.FullPath;

    protected virtual Task<AttachFileInfo[]?> TryPickVisualMediaFiles(string acceptTypes)
        => Task.FromResult<AttachFileInfo[]?>(null);
}
