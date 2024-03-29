namespace ActualChat.Media;

public static class MediaTypeExt
{
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

    public static bool IsSupportedImage(string? contentType)
        => !contentType.IsNullOrEmpty() && ImageExtensionByContentType.ContainsKey(contentType.ToLowerInvariant());
    public static bool IsGif(string? contentType)
        => OrdinalIgnoreCaseEquals(contentType, "image/gif");
    public static bool IsSvg(string? contentType)
        => OrdinalIgnoreCaseEquals(contentType, "image/svg+xml");
    public static bool IsSupportedVideo(string? contentType)
        => !contentType.IsNullOrEmpty() && VideoExtensionByContentType.ContainsKey(contentType.ToLowerInvariant());
    public static bool IsSupportedVisualMedia(string? contentType)
        => IsSupportedImage(contentType) || IsSupportedVideo(contentType);
    public static string? GetFileExtension(string? contentType)
        => !contentType.IsNullOrEmpty() ? ExtensionByContentType.GetValueOrDefault(contentType).NullIfEmpty() : null;
    public static bool IsImage(string? contentType)
        => contentType?.OrdinalIgnoreCaseStartsWith("image/") ?? false;
    public static bool IsVideo(string? contentType)
        => contentType?.OrdinalIgnoreCaseStartsWith("video/") ?? false;
}
