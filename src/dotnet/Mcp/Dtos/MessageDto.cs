namespace ActualChat.Mcp.Dtos;

public sealed record MessageDto(
    long Id,
    long Version,
    long CreatedAt,
    string AuthorId,
    string AuthorName,
    bool IsSystem,
    bool IsStreaming,
    bool IsTranscribed,
    bool IsRemoved,
    string Text);
