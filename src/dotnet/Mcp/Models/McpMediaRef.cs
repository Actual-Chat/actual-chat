namespace ActualChat.Mcp;

public sealed record McpMediaRef(
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

public sealed record McpUploadStatus(
    string UploadId,
    string MediaId,
    long Offset,
    long Length,
    string Stage,
    double Progress,
    McpMediaRef? Media);
