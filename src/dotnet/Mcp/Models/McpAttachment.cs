namespace ActualChat.Mcp;

public sealed record McpAttachment(
    string Id,
    string MediaId,
    string Kind,
    string FileName,
    string ContentType,
    long Length,
    int Width,
    int Height,
    string Url,
    string? PreviewUrl,
    string? ThumbnailUrl);
