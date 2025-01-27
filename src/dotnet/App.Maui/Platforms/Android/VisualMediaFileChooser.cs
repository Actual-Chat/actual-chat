using Android.Webkit;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

public class VisualMediaFileChooser
{
    private readonly MainActivity _mainActivity;

    public VisualMediaFileChooser(MainActivity mainActivity)
        => _mainActivity = mainActivity;

    public bool OnShowFileChooser(
        string[] acceptTypes,
        IValueCallback? filePathCallback)
    {
        if (filePathCallback is null || acceptTypes.Length == 0)
            return false;

        var acceptType = acceptTypes[0];
        if (acceptType.StartsWith("image")) {
            PickVisualMedia(PickVisualMediaKind.Image, filePathCallback);
            return true;
        }
        if (acceptType.StartsWith("video")) {
            PickVisualMedia(PickVisualMediaKind.Video, filePathCallback);
            return true;
        }

        return false;
    }

    private void PickVisualMedia(PickVisualMediaKind kind, IValueCallback filePathCallback)
        => _mainActivity.PickVisualMedia(kind,
                uris => {
                    filePathCallback.OnReceiveValue(uris);
                });
}
