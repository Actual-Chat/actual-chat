namespace ActualChat.App.Maui;

public static class ContentResolver
{
    public const string UriContentScheme = UrlMapper.UriContentScheme;
    public const string FilesContentProvider = "files";
    private const string FilesContentPrefix = $"{UriContentScheme}://{FilesContentProvider}/";

    public static string GetFileUri(string filePath)
        => $"{FilesContentPrefix}{LocalContentRegistry.GetOrAddKey(filePath)}";

    public static bool TryGetFilePathFromUri(string uri, [NotNullWhen(true)] out string? filePath)
    {
        filePath = null;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var uri1))
            return false;

        if (uri1.Host != FilesContentProvider || !uri1.IsDefaultPort)
            return false;

        return LocalContentRegistry.TryGetContentRef(uri1.AbsolutePath.TrimStart('/'), out filePath);
    }
}
