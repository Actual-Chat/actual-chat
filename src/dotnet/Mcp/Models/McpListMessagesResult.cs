namespace ActualChat.Mcp;

public sealed record McpListMessagesResult(
    McpIdRange<long> Range,
    McpIdRange<long> FullRange,
    McpChatMessage[] Messages);
