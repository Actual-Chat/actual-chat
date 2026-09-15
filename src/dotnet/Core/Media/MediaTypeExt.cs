namespace ActualChat.Media;

public static class MediaTypeExt
{
    public static readonly IReadOnlySet<string> AvatarPassthroughContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "image/jpeg",
        "image/png",
        "image/webp",
    };

    public static readonly IReadOnlySet<string> SupportedAvatarContentTypes = new HashSet<string>(AvatarPassthroughContentTypes, StringComparer.OrdinalIgnoreCase) {
        "image/bmp",
        "image/svg+xml",
    };

    private static readonly Dictionary<string, string> ImageExtensionByContentType =
        new (StringComparer.OrdinalIgnoreCase) {
            ["image/bmp"] = ".bmp",
            ["image/jpeg"] = ".jpg",
            ["image/gif"] = ".gif",
            ["image/vnd.microsoft.icon"] = ".ico",
            ["image/png"] = ".png",
            ["image/svg+xml"] = ".svg",
            ["image/webp"] = ".webp",
            ["image/heif"] = ".heif",
            ["image/heic"] = ".heic",
            ["image/avif"] = ".avif",
        };
    private static readonly Dictionary<string, string> VideoExtensionByContentType =
        new (StringComparer.OrdinalIgnoreCase) {
            ["video/mp4"] = ".mp4",
            // webm seems not working on iOS. Probably supported types must be platform-specific
            // ["video/vp8"] = ".webm",
            // ["video/vp9"] = ".webm",
            // ["video/av1"] = ".webm",
            // ["video/webm"] = ".webm",
        };
    private static readonly Dictionary<string, string> ExtensionByContentType =
        new (ImageExtensionByContentType.Concat(VideoExtensionByContentType), StringComparer.OrdinalIgnoreCase);
    // Derived from the tables above rather than MediaMimeTypes, which has no HEIC/HEIF entry: deriving
    // it means an extension always maps back to a type IsSupportedImage/IsSupportedVideo accepts
    private static readonly Dictionary<string, string> ContentTypeByExtension =
        ExtensionByContentType
            .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Key, StringComparer.OrdinalIgnoreCase);

    // Android's gallery picker hands a HEIC over as application/octet-stream, and some browsers send
    // an empty type, which would drop the file out of the image pipeline entirely
    public static string NormalizeContentType(string? contentType, string? fileName)
    {
        if (!contentType.IsNullOrEmpty()
            && !string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return contentType;

        var extension = Path.GetExtension(fileName ?? "");
        return extension.IsNullOrEmpty()
            ? contentType ?? ""
            : ContentTypeByExtension.GetValueOrDefault(extension) ?? contentType ?? "";
    }

    public static bool IsSupportedImage(string? contentType)
        => !contentType.IsNullOrEmpty() && ImageExtensionByContentType.ContainsKey(contentType.ToLower());
    public static bool IsGif(string? contentType)
        => string.Equals(contentType, "image/gif", StringComparison.OrdinalIgnoreCase);
    public static bool IsSvg(string? contentType)
        => string.Equals(contentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase);
    public static bool IsHeif(string? contentType)
        => string.Equals(contentType, "image/heic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "image/heif", StringComparison.OrdinalIgnoreCase);
    public static bool IsSupportedVideo(string? contentType)
        => !contentType.IsNullOrEmpty() && VideoExtensionByContentType.ContainsKey(contentType.ToLower());
    public static bool IsSupportedVisualMedia(string? contentType)
        => IsSupportedImage(contentType) || IsSupportedVideo(contentType);
    public static string? GetFileExtension(string? contentType)
        => !contentType.IsNullOrEmpty() ? ExtensionByContentType.GetValueOrDefault(contentType).NullIfEmpty() : null;
    public static bool IsImage(string? contentType)
        => contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false;
    public static bool IsVideo(string? contentType)
        => contentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ?? false;
    public static bool IsAudio(string? contentType)
        => contentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ?? false;
    public static bool IsVisualMedia(string? contentType)
        => IsImage(contentType) || IsVideo(contentType);
}
