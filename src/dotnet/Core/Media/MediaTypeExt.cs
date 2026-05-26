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

    public static bool IsSupportedImage(string? contentType)
        => !contentType.IsNullOrEmpty() && ImageExtensionByContentType.ContainsKey(contentType.ToLower());
    public static bool IsGif(string? contentType)
        => string.Equals(contentType, "image/gif", StringComparison.OrdinalIgnoreCase);
    public static bool IsSvg(string? contentType)
        => string.Equals(contentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase);
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
