namespace ActualChat.Chat;

public record TextEntryAttachmentUpload(string FileName, byte[] Content, string FileType)
{
    public string Description { get; init; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
}
