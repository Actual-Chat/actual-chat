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

        if (!OrdinalEquals(uri1.Host, FilesContentProvider) || !uri1.IsDefaultPort)
            return false;

        filePath = Uri.UnescapeDataString(uri1.LocalPath.TrimStart('/'));
        return true;
    }
}
