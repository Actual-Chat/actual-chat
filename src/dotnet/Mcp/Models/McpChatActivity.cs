namespace ActualChat.Mcp;

public sealed record McpChatChange(string ChatId, long? AfterId, long LastId);

public sealed record McpWaitForChangesResult(McpChatChange[] Changes, string Cursor);

public sealed record McpUnreadChat(string ChatId, long ReadId, long LastId, long UnreadCount);

public sealed record McpListUnreadChatsResult(McpUnreadChat[] Chats, string Cursor);
