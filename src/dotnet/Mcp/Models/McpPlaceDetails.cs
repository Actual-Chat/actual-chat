namespace ActualChat.Mcp;

public sealed record McpPlaceDetails(
    string Id,
    string Title,
    string Description,
    bool IsPublic,
    string? PictureUrl,
    string? BackgroundUrl,
    int MemberCount,
    string[] Permissions);
