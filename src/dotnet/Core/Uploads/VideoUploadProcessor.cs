using System.Net.Mime;
using FFMpegCore;
using SixLabors.ImageSharp;

namespace ActualChat.Uploads;

public class VideoUploadProcessor : IUploadProcessor
{
    private ILogger<VideoUploadProcessor> Log { get; }

    public VideoUploadProcessor(ILogger<VideoUploadProcessor> log)
        => Log = log;

    public bool Supports(FileInfo file)
        => file.ContentType.OrdinalIgnoreCaseContains("video");

    public async Task<ProcessedFileInfo> Process(FileInfo file, CancellationToken cancellationToken)
    {
        var size = await GetVideoDimensions(file, cancellationToken).ConfigureAwait(false);
        return size != null
            ? new ProcessedFileInfo(file, size)
            : new ProcessedFileInfo(file with { ContentType = MediaTypeNames.Application.Octet, }, null);
    }

    private async Task<Size?> GetVideoDimensions(FileInfo file, CancellationToken cancellationToken)
    {
        try {
            // TODO: analyse video without dumping to FS. This workaround for unix cause ffprobe fails with moov atom not found.
            using var tmp = Disposable.New(Path.GetTempFileName(), File.Delete);
            await File.WriteAllBytesAsync(tmp.Resource, file.Content, cancellationToken).ConfigureAwait(false);
            using var stream = new MemoryStream(file.Content);
            var media = await FFProbe.AnalyseAsync(tmp.Resource, cancellationToken: cancellationToken).ConfigureAwait(false);
            var video = media.PrimaryVideoStream;
            return video is null ? null : new Size(video.Width, video.Height);

        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to extract video info from '{FileName}'", file.FileName);
            return null;
        }
    }
}
