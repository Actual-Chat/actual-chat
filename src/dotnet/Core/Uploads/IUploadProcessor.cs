using Stl.IO;

namespace ActualChat.Uploads;

public interface IUploadProcessor
{
    bool Supports(string contentType);
    Task<ProcessedFile> Process(UploadedFile file, CancellationToken cancellationToken);
}

public sealed record ProcessedFile(UploadedFile File, Size? Size) : IDisposable
{
    public void Dispose()
        => (File as UploadedTempFile)?.Delete();
}

public abstract record UploadedFile(string FileName, string ContentType)
{
    public abstract long Length { get; }
    public abstract Stream Open();

    public async Task<string> GetSHA256HashCode(HashEncoding hashEncoding)
    {
        var stream = Open();
        await using var _ = stream.ConfigureAwait(false);
        return await stream.GetSHA256HashCode(hashEncoding).ConfigureAwait(false);
    }
}

public sealed record UploadedMemoryFile(string FileName, string ContentType, byte[] Data) : UploadedFile(FileName, ContentType)
{
    public override long Length => Data.Length;

    public override Stream Open()
        => new MemoryStream(Data);
}

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
