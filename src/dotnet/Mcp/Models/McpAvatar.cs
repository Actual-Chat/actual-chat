namespace ActualChat.Mcp;

public sealed record McpAvatar(
    string Id,
    string Name,
    string Bio,
    string? PictureUrl,
    bool IsDefault);
