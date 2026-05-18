namespace ActualChat.Mcp.Dtos;

public sealed record ListMessagesResult(
    IdRange<long> Range,
    IdRange<long> FullRange,
    MessageDto[] Messages);
