using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Components;
using Photos;
using PhotosUI;
using UniformTypeIdentifiers;

namespace ActualChat.App.Maui;

public class IosAttachmentFilePicker(IServiceProvider services) : MauiAttachmentFilePicker(services)
{
    private const int MaxSelectionCount = 10;

    private IosPhotoGalleryFiles PhotoGalleryFiles => field ??= Services.GetRequiredService<IosPhotoGalleryFiles>();

    protected override async Task<AttachFileInfo[]?> TryPickVisualMediaFiles(string acceptTypes)
    {
        if (!MediaTypeExt.IsVisualMedia(acceptTypes))
            return [];

        var pickerResults = await PickVisualMedia(acceptTypes).ConfigureAwait(false);
        return await LoadPickedFiles(pickerResults).ConfigureAwait(false);
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
        await controller.PresentViewControllerAsync(picker, true).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    private Task<AttachFileInfo[]> LoadPickedFiles(PHPickerResult[] results)
        => DispatchToMainThread(() => {
            var attachFileInfos = results
                .Select(CreateAttachFileInfo)
                .SkipNullItems()
                .ToArray();
            return Task.FromResult(attachFileInfos);
        });

    private AttachFileInfo? CreateAttachFileInfo(PHPickerResult pickerResult)
    {
        try {
            // Enqueue for background loading - returns MauiFileProvider immediately
            var fileProvider = PhotoGalleryFiles.Enqueue(pickerResult);
            return new AttachFileInfo(fileProvider);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to enqueue picked media file.");
            return null;
        }
    }

    private static PHPickerConfiguration GetConfiguration(string acceptTypes)
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
