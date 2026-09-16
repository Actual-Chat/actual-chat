namespace ActualChat.Mcp;

public sealed record McpConversation(
    string Id,
    string ChatId,
    long StartEntryId,
    long EndEntryId,
    long StartsAt,
    long EndsAt,
    string Title,
    string Description,
    string Summary,
    int MessageCount,
    int AttachmentCount,
    string[] AuthorIds);

public sealed record McpListConversationsResult(McpConversation[] Conversations, string? NextBeforeId);
