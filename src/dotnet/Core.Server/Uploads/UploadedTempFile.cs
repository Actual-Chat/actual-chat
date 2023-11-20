using Stl.IO;

namespace ActualChat.Uploads;

public sealed record UploadedTempFile(FilePath FileName, string ContentType, FilePath TempFilePath) : UploadedFile(FileName, ContentType)
{
    public override long Length { get; init; } = new FileInfo(TempFilePath).Length;

    public override Task<Stream> Open()
        => Task.FromResult<Stream>(File.OpenRead(TempFilePath));

    public void Delete()
        => File.Delete(TempFilePath);

    public bool Equals(UploadedTempFile? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return TempFilePath.Equals(other.TempFilePath);
    }

    public override int GetHashCode()
        => TempFilePath.GetHashCode();
}
