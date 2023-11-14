using Stl.IO;

namespace ActualChat.Uploads;

public interface IUploadProcessor
{
    bool Supports(UploadedFile file);
    Task<ProcessedFile> Process(UploadedFile file, CancellationToken cancellationToken);
}

public sealed record ProcessedFile(UploadedFile File, Size? Size) : IDisposable
{
    public void Dispose()
        => File.Delete();
}

public sealed record UploadedFile(string FileName, string ContentType, long Length, FilePath TempFilePath)
{
    public Stream Open()
        => File.OpenRead(TempFilePath);

    public void Delete()
        => File.Delete(TempFilePath);

    public async Task<string> GetSHA256HashCode(HashEncoding hashEncoding)
    {
        var stream = Open();
        await using var _ = stream.ConfigureAwait(false);
        return await stream.GetSHA256HashCode(hashEncoding).ConfigureAwait(false);
    }
}
