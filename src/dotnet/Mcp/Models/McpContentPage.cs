namespace ActualChat.Mcp;

public sealed record McpContentPage<T>(
    string PeriodKey,
    int PageIndex,
    int PageCount,
    string? NextPeriodKey,
    T[] Items);

public sealed record McpMediaItem(
    long EntryId,
    long At,
    string MediaId,
    string Kind,
    string FileName,
    string ContentType,
    long Length,
    string Url,
    string? ThumbnailUrl);

public sealed record McpFileItem(
    long EntryId,
    long At,
    string MediaId,
    string FileName,
    string ContentType,
    long Length,
    string Url);

public sealed record McpLinkItem(
    long EntryId,
    long At,
    string Url,
    string? Title,
    string? Description);
