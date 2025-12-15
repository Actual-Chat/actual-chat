using System.Diagnostics.CodeAnalysis;

namespace ActualChat.App.Maui;

public static class ContentResolver
{
    public const string UriContentScheme = "content://";
    public const string FilesContentProvider = "files";

    public static string GetFileUri(string filePath)
    {
        const string prefix = "content://files/";
        return prefix + Uri.EscapeDataString(filePath);
    }

    public static bool TryGetFilePathFromUri(string uri, [NotNullWhen(true)] out string? filePath)
    {
        filePath = null;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var uri1))
            return false;

        if (!string.Equals(uri1.Host, FilesContentProvider, StringComparison.Ordinal) || !uri1.IsDefaultPort)
            return false;

        // IMPORTANT(MacCatalyst): keep absolute paths.
        // For paths like `/private/...` (Files/iCloud), trimming the leading '/' turns it into
        // a relative path and `File.Exists` fails.
        var path = Uri.UnescapeDataString(uri1.LocalPath);
        if (path.Length >= 2 && path[0] == '/' && path[1] == '/')
            path = "/" + path.TrimStart('/');
        filePath = path;
        return true;
    }
}
