namespace ActualChat.Mcp;

public static class McpModelExt
{
    public static McpIdRange<long> ToMcpModel(this Range<long> range)
        => range.IsEmptyOrNegative
            ? new McpIdRange<long>(range.Start, range.Start - 1)
            : new McpIdRange<long>(range.Start, range.End - 1);

    public static McpChatMessage ToMcpModel(
        this ChatEntry entry,
        Dictionary<AuthorId, Author?> authorById)
    {
        var authorName = authorById.GetValueOrDefault(entry.AuthorId)?.Avatar?.Name ?? "";
        var isStreaming = entry.IsContentStreaming;
        var text = isStreaming ? "" : entry.Content;
        return new McpChatMessage(
            entry.LocalId,
            entry.Version,
            (long)(entry.BeginsAt - Moment.EpochStart).TotalMilliseconds,
            entry.AuthorId.Value,
            authorName,
            entry.IsSystemEntry,
            isStreaming,
            entry.HasAudio,
            entry.IsRemoved,
            text);
    }
}
