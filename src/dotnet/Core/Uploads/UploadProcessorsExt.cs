using Stl.IO;

namespace ActualChat.Uploads;

public static class UploadProcessorsExt
{
    public static async Task<ProcessedFile> Process(this IEnumerable<IUploadProcessor> processors, FilePath fileName, string contentType, Stream stream, CancellationToken cancellationToken)
    {
        var uploadedFile = await DumpToTempFile(fileName, contentType, stream, cancellationToken).ConfigureAwait(false);
        var processor = processors.FirstOrDefault(x => x.Supports(uploadedFile));
        if (processor == null)
            return new ProcessedFile(uploadedFile, null);

        var processedFile = await processor.Process(uploadedFile, cancellationToken).ConfigureAwait(false);
        if (processedFile.File.TempFilePath != uploadedFile.TempFilePath)
            uploadedFile.Delete();

        return processedFile;
    }

    private static async Task<UploadedFile> DumpToTempFile(FilePath fileName, string contentType, Stream stream, CancellationToken cancellationToken)
    {
        fileName = fileName.FileNameWithoutExtension + "_" + Guid.NewGuid() + fileName.Extension;
        var tempFilePath = FilePath.GetApplicationTempDirectory() & fileName;
        var target = File.OpenWrite(tempFilePath);
        await using var _ = target.ConfigureAwait(false);
        await stream.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        target.Position = 0;
        return new UploadedFile(fileName, contentType, new FileInfo(tempFilePath).Length, tempFilePath);
    }
}
