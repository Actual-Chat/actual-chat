using AndroidUri = Android.Net.Uri;
using FileProvider = Microsoft.Maui.Storage.FileProvider;

namespace ActualChat.App.Maui;

public static class FileProviderExt
{
    private static MethodInfo? _miGetUriForFile;

    public static AndroidUri GetUriForFile(Java.IO.File file)
    {
        var bindingFlags = BindingFlags.Static | BindingFlags.NonPublic;
        _miGetUriForFile ??= typeof(FileProvider).GetMethod("GetUriForFile", bindingFlags);
        if (_miGetUriForFile == null)
            throw new InvalidOperationException("Can not find GetUriForFile static method on class Microsoft.Maui.Storage.FileProvider.");
        return (AndroidUri)_miGetUriForFile.Invoke(null, new object[] { file })!;
    }
}
