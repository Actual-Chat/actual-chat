using ActualChat.External;

namespace ActualChat.Mcp;

public sealed record McpNotification(
    long Seq,
    string Kind,
    long At,
    string? ChatId,
    long? EntryId,
    string? AuthorId,
    string Title,
    string Text,
    ExternalMessage? Message);

public sealed record McpListNotificationsResult(McpNotification[] Items, long? NextAfterSeq);
