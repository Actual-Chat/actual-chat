using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

public class VisualMediaFileChooser
{
    private readonly MainActivity _mainActivity;

    public VisualMediaFileChooser(MainActivity mainActivity)
        => _mainActivity = mainActivity;

    public bool OnShowFileChooser(
        string acceptTypes,
        Action<Uri[]> callback)
    {
        if (acceptTypes.OrdinalStartsWith("image")) {
            PickVisualMedia(PickVisualMediaKind.Image, callback);
            return true;
        }
        if (acceptTypes.OrdinalStartsWith("video")) {
            PickVisualMedia(PickVisualMediaKind.Video, callback);
            return true;
        }
        return false;
    }

    private void PickVisualMedia(PickVisualMediaKind kind, Action<Uri[]> callback)
        => _mainActivity.PickVisualMedia(kind, callback);
}
