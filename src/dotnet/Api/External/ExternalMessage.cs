namespace ActualChat.External;

public sealed record ExternalAuthor(string Id, string Name, string? AvatarUrl);

public sealed record ExternalAttachment(
    string MediaId, string Kind, string FileName, string ContentType, long Length,
    int Width, int Height, string Url, string? PreviewUrl, string? ThumbnailUrl);

// Kind is one of "user" | "api" | "webhook" | "bot"
public sealed record ExternalOrigin(string Kind, string? WebHookId = null);

public sealed record ExternalMessage(
    long Id, long Version, long CreatedAt, ExternalAuthor Author,
    bool IsSystem, bool IsStreaming, bool IsTranscribed, bool IsRemoved,
    string? Text, bool? TextTruncated, ExternalAttachment[] Attachments,
    long? RepliedToId, string[] Mentions, string Url, ExternalOrigin Origin);
