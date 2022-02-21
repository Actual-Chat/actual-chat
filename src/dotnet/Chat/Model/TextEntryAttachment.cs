namespace ActualChat.Chat;

public record TextEntryAttachment
{
    public Symbol ChatId { get; init; }
    public long EntryId { get; init; }
    public int Index { get; init; }
    public long Version { get; init; }
    public string ContentId { get; init; } = "";
    public long Length { get; init; }
    public string FileName { get; init; } = "";
    public string ContentType { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public string Url => "/api/content/" + ContentId;
    public string ProxyUrl => Url;
}
