using System.Net.Mime;
using ActualLab.Generators;
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

    public static async Task<UploadedTempFile> DumpToTempFile(this UploadedFile file, CancellationToken cancellationToken)
    {
        var displayedName = file.GetDisplayFileName();
        var tempFilePath = NewTempFilePath();
        var source = await file.Open().ConfigureAwait(false);
        await using var _ = source.ConfigureAwait(false);
        await source.CopyToFile(tempFilePath, cancellationToken).ConfigureAwait(false);
        return new UploadedTempFile(displayedName, file.ContentType, tempFilePath);
    }

    internal static FilePath GetDisplayFileName(this UploadedFile file)
        => Path.GetFileName(file.FileName.Value);

    internal static FilePath NewTempFilePath()
        => FilePathValidator.GetContainedPath(
            FilePath.GetApplicationTempDirectory(),
            RandomStringGenerator.Default.Next());
}
