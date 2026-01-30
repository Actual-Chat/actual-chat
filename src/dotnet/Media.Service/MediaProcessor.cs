using ActualChat.Uploads;

namespace ActualChat.Media;

public class MediaProcessor(IServiceProvider services) : IMediaProcessor
{
    private IReadOnlyCollection<IUploadProcessor> UploadProcessors { get; }
        = services.GetRequiredService<IEnumerable<IUploadProcessor>>().ToList();

    public async Task<ProcessedFile> ProcessUpload(UploadedFile uploadedFile, CancellationToken cancellationToken)
        => await UploadProcessors.Process(uploadedFile, cancellationToken).ConfigureAwait(false);
}
