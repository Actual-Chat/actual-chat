namespace ActualChat.Chat;

public static class MediaTypeExt
{
    private static readonly HashSet<string> _supportedImageTypes = ["image/png", "image/jpeg", "image/svg+xml", "image/webp", "image/gif", "image/bmp", "image/heif", "image/heic", "image/heif", "image/avif"];
    private static readonly HashSet<string> _supportedVideoTypes = ["video/mp4", "video/vp8", "video/vp9", "video/av1"];
    private static readonly Dictionary<string, string> ExtensionByContentType =
        new (StringComparer.OrdinalIgnoreCase) {
            // images
            ["image/bmp"] = ".bmp",
            ["image/jpeg"] = ".jpg",
            ["image/vnd.microsoft.icon"] = ".ico",
            ["image/png"] = ".png",
            ["image/svg+xml"] = ".svg",
            ["image/webp"] = ".webp",
            // videos
            ["video/mp4"] = ".mp4",
            ["video/vp8"] = ".webm",
            ["video/vp9"] = ".webm",
            ["video/av1"] = ".webm",
            ["video/webm"] = ".webm",
        };

    public static bool IsSupportedImage(string? contentType)
        => !contentType.IsNullOrEmpty() && _supportedImageTypes.Contains(contentType.ToLowerInvariant());
    public static bool IsGif(string? contentType)
        => OrdinalIgnoreCaseEquals(contentType, "image/gif");
    public static bool IsSvg(string? contentType)
        => OrdinalIgnoreCaseEquals(contentType, "image/svg+xml");
    public static bool IsSupportedVideo(string? contentType)
        => !contentType.IsNullOrEmpty() && _supportedVideoTypes.Contains(contentType.ToLowerInvariant());
    public static bool IsSupportedVisualMedia(string? contentType)
        => IsSupportedImage(contentType) || IsSupportedVideo(contentType);
    public static string? GetFileExtension(string? contentType)
    => !contentType.IsNullOrEmpty() ? ExtensionByContentType.GetValueOrDefault(contentType).NullIfEmpty() : null;
}
