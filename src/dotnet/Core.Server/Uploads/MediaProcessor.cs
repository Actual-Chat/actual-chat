namespace ActualChat.Uploads;

public sealed class MediaProcessor(IServiceProvider services) : IMediaProcessor
{
    private IReadOnlyCollection<IUploadProcessor> Processors { get; }
        = services.GetRequiredService<IEnumerable<IUploadProcessor>>().ToList();
    private ILogger Log =>  field ??= services.LogFor(GetType());

    public async Task<ProcessedFile> ProcessUpload(UploadedFile uploadedFile, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var processor = Processors.FirstOrDefault(x => x.Supports(uploadedFile.ContentType));
        if (processor is null)
            // no need to process the file
            return new ProcessedFile(uploadedFile, null);

        var sw = Stopwatch.GetTimestamp();
        var processedFile = await processor.Process(uploadedFile, progress, cancellationToken).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(sw);
        Log.LogInformation($"Processed '{uploadedFile.FileName}' in {elapsed.TotalMilliseconds}ms");

        progress?.Report(100);
        return processedFile;
    }
}
