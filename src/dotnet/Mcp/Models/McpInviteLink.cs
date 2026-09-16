namespace ActualChat.Mcp;

public sealed record McpInviteLink(string Id, string Url, int Remaining, long ExpiresOn);
