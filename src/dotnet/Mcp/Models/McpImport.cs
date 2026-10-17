namespace ActualChat.Mcp;

public sealed record McpImportStatus(
    bool Active,
    string? ImportId = null,
    string? Scope = null,
    string? PlaceId = null,
    string? StartedBy = null,
    long? StartedAt = null,
    int? ConsentingCount = null,
    int? NonConsentingCount = null,
    double? ConsentPercent = null,
    bool? MyConsent = null,
    McpImportPendingMember[]? Pending = null);

public sealed record McpImportPendingMember(string UserId, string Name, string? PeerChatId);

public sealed record McpImportSession(
    string ImportId,
    string ChatId,
    string Scope,
    string? StartedBy,
    long StartedAt);

public sealed record McpImportEndResult(int NonConsentingCount);

public sealed record McpWipeHistoryResult(long FromId, long EndId, bool IsPurged);

public sealed record McpImportMessage(
    string AuthorUserId,
    long SentAt,
    string Text,
    long? ReplyToId = null,
    string[]? UploadIds = null);

public sealed record McpImportMessageResult(long? Id, string? Error);

public sealed record McpImportMessagesResult(McpImportMessageResult[] Items);
