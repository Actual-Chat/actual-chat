namespace ActualChat.Mcp;

public sealed record McpChatMessage(
    long Id,
    long Version,
    long CreatedAt,
    string AuthorId,
    string AuthorName,
    bool IsSystem,
    bool IsStreaming,
    bool IsTranscribed,
    bool IsRemoved,
    string Text,
    McpAttachment[] Attachments);
