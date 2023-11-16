using Stl.IO;

namespace ActualChat.Uploads;

public static class UploadProcessorsExt
{
    public static async Task<ProcessedFile> Process(this IEnumerable<IUploadProcessor> processors, UploadedFile file, CancellationToken cancellationToken)
    {
        var processor = processors.FirstOrDefault(x => x.Supports(file.ContentType));
        if (processor is null)
            // no need to dump file to file system
            return new ProcessedFile(file, null);

        var tempFile = await DumpToTempFile(file, cancellationToken).ConfigureAwait(false);
        var processedFile = await processor.Process(tempFile, cancellationToken).ConfigureAwait(false);
        if (tempFile != processedFile.File)
            tempFile.Delete();

        return processedFile;
    }

    private static async Task<UploadedTempFile> DumpToTempFile(UploadedFile file, CancellationToken cancellationToken)
    {
        var tempFileName = Guid.NewGuid() + "_" + file.FileName;
        var tempFilePath = FilePath.GetApplicationTempDirectory() & tempFileName;
        var target = File.OpenWrite(tempFilePath);
        await using var _1 = target.ConfigureAwait(false);
        var source = await file.Open().ConfigureAwait(false);
        await using var _2 = source.ConfigureAwait(false);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        target.Position = 0;
        return new UploadedTempFile(file.FileName, file.ContentType, tempFilePath);
    }
}
