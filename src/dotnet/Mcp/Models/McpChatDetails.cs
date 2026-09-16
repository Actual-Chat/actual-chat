namespace ActualChat.Mcp;

public sealed record McpChatDetails(
    string Id,
    string Kind,
    string Title,
    string Description,
    bool IsPublic,
    string? PlaceId,
    string? PictureUrl,
    int MemberCount,
    string[] Permissions);
