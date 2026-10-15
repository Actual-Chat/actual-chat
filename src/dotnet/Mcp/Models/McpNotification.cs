using ActualChat.External;

namespace ActualChat.Mcp;

public sealed record McpNotification(
    string Id,
    string Kind,
    long At,
    string? ChatId,
    long? EntryId,
    string? AuthorId,
    string Title,
    string Text,
    ExternalMessage? Message);

public sealed record McpListNotificationsResult(McpNotification[] Items, string Version);
