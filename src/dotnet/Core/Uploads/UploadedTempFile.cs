using Stl.IO;

namespace ActualChat.Uploads;

public sealed record UploadedTempFile(string FileName, string ContentType, FilePath TempFilePath) : UploadedFile(FileName, ContentType)
{
    public override long Length => new FileInfo(TempFilePath).Length;

    public override Stream Open()
        => File.OpenRead(TempFilePath);

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
