using ActualLab.IO;
namespace ActualChat.Uploads;

public class MediaProcessor(IServiceProvider services) : IMediaProcessor
{
    private IReadOnlyCollection<IUploadProcessor> UploadProcessors { get; }
        = services.GetRequiredService<IEnumerable<IUploadProcessor>>().ToList();

    public async Task<ProcessedFile> ProcessUpload(UploadedFile uploadedFile, CancellationToken cancellationToken)
    {
        var processor = UploadProcessors.FirstOrDefault(x => x.Supports(uploadedFile.ContentType));
        if (processor is null)
            // no need to dump file to file system
            return new ProcessedFile(uploadedFile, null);

        var tempFile = await DumpToTempFile(uploadedFile, cancellationToken).ConfigureAwait(false);
        var processedFile = await processor.Process(tempFile, cancellationToken).ConfigureAwait(false);
        if (tempFile != processedFile.File)
            tempFile.Delete();

        return processedFile;
    }

    private static async Task<UploadedTempFile> DumpToTempFile(UploadedFile file, CancellationToken cancellationToken)
    {
        var tempFileName = Guid.NewGuid() + "_" + file.FileName;
        var tempFilePath = FilePath.GetApplicationTempDirectory() & FileExt.ShortenFileName(tempFileName);
        var target = File.OpenWrite(tempFilePath);
        await using var _1 = target.ConfigureAwait(false);
        var source = await file.Open().ConfigureAwait(false);
        await using var _2 = source.ConfigureAwait(false);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        target.Position = 0;
        return new UploadedTempFile(file.FileName, file.ContentType, tempFilePath);
    }
}
