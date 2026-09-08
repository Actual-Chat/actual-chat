using AppKit;
using CoreGraphics;
using PhotosUI;

namespace ActualChat.App.Maui;

public partial class AppleAttachmentFilePicker
{
    private static readonly CGSize PickerSheetSize = new(900, 600);

    private partial Task<PHPickerResult[]> Present(PHPickerViewController picker)
    {
        // The labs backend leaves NSWindow.ContentViewController unset for a plain ContentPage,
        // so there is no view controller to present from; the picker gets its own sheet window.
        var app = NSApplication.SharedApplication;
        var window = app.KeyWindow ?? app.MainWindow;
        if (window is null) {
            Log.LogWarning("Failed to open media picker: no key or main window.");
            return Task.FromResult<PHPickerResult[]>([]);
        }

        var tcs = TaskCompletionSourceExt.New<PHPickerResult[]>();
        var pickerWindow = NSWindow.GetWindowWithContentViewController(picker);
        pickerWindow.SetContentSize(PickerSheetSize);
        picker.Delegate = new PickerDelegate(tcs, () => window.EndSheet(pickerWindow));
        window.BeginSheet(pickerWindow, _ => { });
        return tcs.Task;
    }
}
