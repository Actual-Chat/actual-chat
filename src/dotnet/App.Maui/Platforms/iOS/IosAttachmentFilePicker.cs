using ActualChat.App.Maui.Services;
using ActualChat.Maui;
using ActualChat.Media;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;
using Photos;
using PhotosUI;
using UniformTypeIdentifiers;

namespace ActualChat.App.Maui;

public class IosAttachmentFilePicker(IServiceProvider services) : MauiAttachmentFilePicker(services)
{
    private const int MaxSelectionCount = 10;
    private static readonly FilePath AttachmentsDirectoryName = Path.Combine(FileSystem.CacheDirectory, "attachments");

    protected override async Task<AttachFileInfo[]?> TryPickVisualMediaFiles(string acceptTypes)
    {
        if (!MediaTypeExt.IsVisualMedia(acceptTypes))
            return [];

        var pickerResults = await PickVisualMedia(acceptTypes).ConfigureAwait(false);
        var preferredContentType = MediaTypeExt.IsImage(acceptTypes) ? UTTypes.Image : UTTypes.Movie;
        return await LoadPickedFiles(pickerResults, preferredContentType).ConfigureAwait(false);
    }

    private async Task<PHPickerResult[]> PickVisualMedia(string acceptTypes)
    {
        var configuration = GetConfiguration(acceptTypes);
        var tcs = TaskCompletionSourceExt.New<PHPickerResult[]>();
        var controller = Platform.GetCurrentUIViewController();
        if (controller is null) {
            Log.LogWarning("Failed to open media picker: current view controller not available.");
            tcs.TrySetResult([]);
            return [];
        }
        var picker = new PHPickerViewController(configuration) {
            Delegate = new PickerDelegate(tcs),
        };
        controller.PresentViewController(picker, true, null);
        return await tcs.Task.ConfigureAwait(false);
    }

    private Task<AttachFileInfo[]> LoadPickedFiles(PHPickerResult[] results, UTType preferredContentType)
        => DispatchToMainThread(async () => {
            var pickedMediaFiles = await results.Select(x => LoadPickedMedia(x, preferredContentType)).Collect().ConfigureAwait(false);
            return pickedMediaFiles.SkipNullItems().ToArray();
        });

    private async Task<AttachFileInfo?> LoadPickedMedia(PHPickerResult pickerResult, UTType contentType)
    {
        var item = pickerResult.ItemProvider;
        try {
            var representation = await item
                .LoadInPlaceFileRepresentationAsync(contentType.Identifier)
                .ConfigureAwait(false);
            FilePath fileName = item.SuggestedName.NullIfEmpty() ?? representation.Path.FileName;
            var tmpFilePath = AttachmentsDirectoryName | fileName.ToUnique();
            await representation.Copy(tmpFilePath).ConfigureAwait(false);
            var fileProvider = new MauiFileProvider {
                FileRef = tmpFilePath,
                Metadata = new() {
                    FileName = fileName,
                    FileType = representation.ImplyMimeType(item),
                    Length = new FileInfo(tmpFilePath).Length,
                },
            };
            fileProvider.Initialize(Services);
            return new AttachFileInfo(fileProvider);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to load picked media file.");
            return null;
        }
    }

    private PHPickerConfiguration GetConfiguration(string acceptTypes)
    {
        var filter = MediaTypeExt.IsImage(acceptTypes) ? PHPickerFilter.ImagesFilter : PHPickerFilter.VideosFilter;
        return new PHPickerConfiguration(PHPhotoLibrary.SharedPhotoLibrary) {
            SelectionLimit = MaxSelectionCount,
            Filter = filter,
        };
    }

    private sealed class PickerDelegate(TaskCompletionSource<PHPickerResult[]> tcs)
        : PHPickerViewControllerDelegate
    {
        public override void DidFinishPicking(PHPickerViewController picker, PHPickerResult[] results)
        {
            picker.DismissViewController(true, null);
            tcs.TrySetResult(results);
        }
    }
}
