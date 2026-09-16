namespace ActualChat.Mcp;

public sealed record McpFoundMessage(string ChatId, long EntryId, string Text);

public sealed record McpFoundContact(string Kind, string Id, string Title);
