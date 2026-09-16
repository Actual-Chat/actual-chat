namespace ActualChat.Mcp;

public sealed record McpMember(string AuthorId, string? UserId, string Name, bool IsOwner);

public sealed record McpListMembersResult(McpMember[] Members);
