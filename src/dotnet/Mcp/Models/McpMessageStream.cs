namespace ActualChat.Mcp;

public sealed record McpMessageStream(
    string StreamId,
    long EntryId,
    int Offset,
    double? SpeechBacklog,
    bool IsFinished);
