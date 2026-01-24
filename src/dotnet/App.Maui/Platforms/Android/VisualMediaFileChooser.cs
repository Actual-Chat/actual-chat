using ActualChat.Media;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

public class VisualMediaFileChooser(MainActivity mainActivity)
{
    public bool OnShowFileChooser(
        string acceptTypes,
        Action<Uri[]> callback)
    {
        if (MediaTypeExt.IsImage(acceptTypes)) {
            PickVisualMedia(PickVisualMediaKind.Image, callback);
            return true;
        }
        if (MediaTypeExt.IsVideo(acceptTypes)) {
            PickVisualMedia(PickVisualMediaKind.Video, callback);
            return true;
        }
        return false;
    }

    private void PickVisualMedia(PickVisualMediaKind kind, Action<Uri[]> callback)
        => mainActivity.PickVisualMedia(kind, callback);
}
