using AndroidUri = Android.Net.Uri;
using FileProvider = Microsoft.Maui.Storage.FileProvider;

namespace ActualChat.App.Maui;

public static class FileProviderExt
{
    public static AndroidUri GetUriForFile(Java.IO.File file)
        => GetUriForFileInternal(null, file);

    // Private methods

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetUriForFile")]
    private static extern AndroidUri GetUriForFileInternal(FileProvider? _, Java.IO.File file);
}
