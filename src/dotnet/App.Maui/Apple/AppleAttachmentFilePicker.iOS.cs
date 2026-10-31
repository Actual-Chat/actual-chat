using PhotosUI;

namespace ActualChat.App.Maui;

public partial class AppleAttachmentFilePicker
{
    private partial async Task<PHPickerResult[]> Present(PHPickerViewController picker)
    {
        var controller = Platform.GetCurrentUIViewController();
        if (controller is null) {
            Log.LogWarning("Failed to open media picker: current view controller not available.");
            return [];
        }

        var tcs = TaskCompletionSourceExt.New<PHPickerResult[]>();
        picker.Delegate = new PickerDelegate(tcs, () => picker.DismissViewController(true, null));
        await controller.PresentViewControllerAsync(picker, true).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }
}
