using ActualLab.IO;

namespace ActualChat.Uploads;

public sealed record UploadedStreamFile(
    FilePath FileName,
    string ContentType,
    long Length,
    Func<Task<Stream>> GetStream) : UploadedFile(FileName, ContentType)
{
    public override Task<Stream> Open()
        => GetStream();
}
