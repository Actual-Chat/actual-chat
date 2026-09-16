namespace ActualChat.Mcp;

public sealed record McpAccount(
    string UserId,
    string Name,
    string AvatarId,
    string AvatarName,
    string? AvatarUrl);
