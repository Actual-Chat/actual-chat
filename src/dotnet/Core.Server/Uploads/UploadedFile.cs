using System.Net.Mime;
using ActualLab.IO;

namespace ActualChat.Uploads;

/// <summary>
/// Base class for uploaded file representations.
/// </summary>
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

public static class UploadedFileExt
{
    public static T AsBinaryFile<T>(this T file) where T : UploadedFile
        => file with { ContentType = MediaTypeNames.Application.Octet };
}
