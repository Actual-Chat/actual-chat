namespace ActualChat.Chat;

public static class MediaTypeExt
{
    private static readonly HashSet<string> _supportedImageTypes = ["image/png", "image/jpeg", "image/svg+xml", "image/webp", "image/gif", "image/bmp", "image/heif", "image/heic", "image/heif", "image/avif"];
    private static readonly HashSet<string> _supportedVideoTypes = ["video/mp4", "video/vp8", "video/vp9", "video/av1"];

    public static bool IsSupportedImage(string? contentType)
        => !contentType.IsNullOrEmpty() && _supportedImageTypes.Contains(contentType.ToLowerInvariant());
    public static bool IsGif(string? contentType)
        => OrdinalIgnoreCaseEquals(contentType, "image/gif");
    public static bool IsSvg(string? contentType)
        => OrdinalIgnoreCaseEquals(contentType, "image/svg+xml");
    public static bool IsSupportedVideo(string? contentType)
        => !contentType.IsNullOrEmpty() && _supportedVideoTypes.Contains(contentType.ToLowerInvariant());
}
