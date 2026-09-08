using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Components;
using Photos;
using PhotosUI;

namespace ActualChat.App.Maui;

public partial class AppleAttachmentFilePicker(IServiceProvider services) : MauiAttachmentFilePicker(services)
{
    private const int MaxSelectionCount = 10;

    private ApplePhotoGalleryFiles PhotoGalleryFiles => field ??= Services.GetRequiredService<ApplePhotoGalleryFiles>();

    protected override async Task<AttachFileInfo[]?> TryPickVisualMediaFiles(string acceptTypes)
    {
        if (!MediaTypeExt.IsVisualMedia(acceptTypes))
            return [];

        var pickerResults = await PickVisualMedia(acceptTypes).ConfigureAwait(false);
        return await LoadPickedFiles(pickerResults).ConfigureAwait(false);
    }

    private Task<PHPickerResult[]> PickVisualMedia(string acceptTypes)
        => DispatchToMainThread(() => {
            var picker = new PHPickerViewController(GetConfiguration(acceptTypes));
            return Present(picker);
        });

    // Platform-specific: UIKit presents the picker modally, AppKit hosts it in a sheet window
    private partial Task<PHPickerResult[]> Present(PHPickerViewController picker);

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
        var isImage = MediaTypeExt.IsImage(acceptTypes);
        var configuration = new PHPickerConfiguration(PHPhotoLibrary.SharedPhotoLibrary) {
            SelectionLimit = MaxSelectionCount,
            Filter = isImage ? PHPickerFilter.ImagesFilter : PHPickerFilter.VideosFilter,
        };
#if MACOS
        // The automatic mode's H.264 rendition of an HEVC camera video has no loader on the macOS
        // picker ("No loader block available for type public.mpeg-4"); the original loads fine,
        // and AppleVideoTranscoder converts it at upload time, as on iOS
        if (!isImage)
            configuration.PreferredAssetRepresentationMode = PHPickerConfigurationAssetRepresentationMode.Current;
#endif
        return configuration;
    }

    // Nested types

    private sealed class PickerDelegate(TaskCompletionSource<PHPickerResult[]> tcs, Action dismiss)
        : PHPickerViewControllerDelegate
    {
        public override void DidFinishPicking(PHPickerViewController picker, PHPickerResult[] results)
        {
            dismiss();
            tcs.TrySetResult(results);
        }
    }
}
