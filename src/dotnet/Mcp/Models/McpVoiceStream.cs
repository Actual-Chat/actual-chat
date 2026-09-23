namespace ActualChat.Mcp;

public sealed record McpVoiceStream(
    string StreamId,
    long EntryId,
    int TextOffset,
    long AudioBytes,
    bool IsFinished);
