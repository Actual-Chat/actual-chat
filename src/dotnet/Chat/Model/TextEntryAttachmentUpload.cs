namespace ActualChat.Chat;

public record TextEntryAttachmentUpload(string FileName, byte[] Content, string FileType)
{
    public string Description { get; init; } = "";
}
