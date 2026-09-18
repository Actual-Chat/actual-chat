using ActualChat.External;

namespace ActualChat.Mcp;

public sealed record McpListMessagesResult(
    McpIdRange<long> Range,
    McpIdRange<long> FullRange,
    ExternalMessage[] Messages);
