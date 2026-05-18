namespace ActualChat.Mcp.Dtos;

public sealed record IdRange<T>(T FirstId, T LastId);
