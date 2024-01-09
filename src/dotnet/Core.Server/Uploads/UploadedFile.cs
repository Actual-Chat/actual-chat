using ActualLab.IO;

namespace ActualChat.Uploads;

public abstract record UploadedFile(FilePath FileName, string ContentType)
{
    public abstract long Length { get; init; }
    public abstract Task<Stream> Open();

    public async Task<string> GetSHA256HashCode(HashEncoding hashEncoding)
    {
        var stream = await Open().ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        return await stream.GetSHA256HashCode(hashEncoding).ConfigureAwait(false);
    }
}
