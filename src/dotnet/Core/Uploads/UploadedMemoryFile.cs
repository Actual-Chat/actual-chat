namespace ActualChat.Uploads;

public sealed record UploadedMemoryFile(string FileName, string ContentType, byte[] Data) : UploadedFile(FileName, ContentType)
{
    public override long Length => Data.Length;

    public override Stream Open()
        => new MemoryStream(Data);
}
