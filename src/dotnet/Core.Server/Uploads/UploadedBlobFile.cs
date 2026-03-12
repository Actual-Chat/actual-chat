using ActualLab.IO;

namespace ActualChat.Uploads;

public sealed record UploadedBlobFile(
    FilePath FileName,
    string ContentType,
    long Length,
    string BlobPath,
    Func<Task<Stream>> GetStream) : UploadedFile(FileName, ContentType)
{
    public override Task<Stream> Open() => GetStream();
}
