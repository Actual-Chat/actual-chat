namespace ActualChat.Mcp;

public sealed record McpMessageStream(
    string StreamId,
    long EntryId,
    int Offset,
    bool IsFinished);
