using ActualLab.IO;

namespace ActualChat.Uploads;

public abstract record UploadedFile(FilePath FileName, string ContentType)
{
    public abstract long Length { get; init; }
    public abstract Task<Stream> Open();

    public async Task<T> Process<T>(Func<Stream, Task<T>> processor)
    {
        var stream = await Open().ConfigureAwait(false);
        await using var _ = stream.ConfigureAwait(false);
        return await processor.Invoke(stream).ConfigureAwait(false);
    }
}
