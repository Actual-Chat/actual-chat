using Stl.IO;

namespace ActualChat.Uploads;

public static class UploadProcessorsExt
{
    public static async Task<ProcessedFile> Process(this IEnumerable<IUploadProcessor> processors, FilePath fileName, string contentType, Stream stream, CancellationToken cancellationToken)
    {
        var processor = processors.FirstOrDefault(x => x.Supports(contentType));
        if (processor is null) {
            // no need to dump file to file system
            var memoryFile = await ToMemoryFile(fileName, contentType, stream, cancellationToken).ConfigureAwait(false);
            return new ProcessedFile(memoryFile, null);
        }

        var tempFile = await DumpToTempFile(fileName, contentType, stream, cancellationToken).ConfigureAwait(false);
        var processedFile = await processor.Process(tempFile, cancellationToken).ConfigureAwait(false);
        if (tempFile != processedFile.File)
            tempFile.Delete();

        return processedFile;
    }

    private static async Task<UploadedTempFile> DumpToTempFile(FilePath fileName, string contentType, Stream stream, CancellationToken cancellationToken)
    {
        var tempFileName = fileName.FileNameWithoutExtension + "_" + Guid.NewGuid() + fileName.Extension;
        var tempFilePath = FilePath.GetApplicationTempDirectory() & tempFileName;
        var target = File.OpenWrite(tempFilePath);
        await using var _ = target.ConfigureAwait(false);
        await stream.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        target.Position = 0;
        return new UploadedTempFile(fileName, contentType, tempFilePath);
    }

    private static async Task<UploadedMemoryFile> ToMemoryFile(FilePath fileName, string contentType, Stream stream, CancellationToken cancellationToken)
    {
        var target = new MemoryStream();
        await using var _ = target.ConfigureAwait(false);
        await stream.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        target.Position = 0;
        return new UploadedMemoryFile(fileName, contentType, target.ToArray());
    }
}
