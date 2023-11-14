using System.Net.Mime;
using FFMpegCore;
using FFMpegCore.Enums;

namespace ActualChat.Uploads;

public class VideoUploadProcessor(ILogger<VideoUploadProcessor> log) : IUploadProcessor
{
    private ILogger<VideoUploadProcessor> Log { get; } = log;

    public bool Supports(UploadedFile file)
        => file.ContentType.OrdinalIgnoreCaseContains("video");

    public async Task<ProcessedFile> Process(UploadedFile file, CancellationToken cancellationToken)
    {
        var (needConversion, size) = await GetVideoInfo(file, cancellationToken).ConfigureAwait(false);
        if (size == null)
            file = file with { ContentType = MediaTypeNames.Application.Octet };
        if (!needConversion)
            return new ProcessedFile(file, size);

        try {
            var convertedFilePath = Path.ChangeExtension(file.TempFilePath, ".converted.mp4");
            await FFMpegArguments.FromFileInput(file.TempFilePath)
                .OutputToFile(convertedFilePath,
                    false,
                    options => options.WithVideoCodec(VideoCodec.LibX264)
                        .WithFastStart()
                        .WithVariableBitrate(4))
                .ProcessAsynchronously()
                .ConfigureAwait(false);
            return new ProcessedFile(
                new UploadedFile(
                    Path.ChangeExtension(file.FileName, ".mp4"),
                    "video/mp4",
                    new FileInfo(convertedFilePath).Length,
                    convertedFilePath),
                size);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        }
        catch (Exception e) {
            Log.LogError(e, "Could not convert uploaded video '{File}'", file.FileName);
            return new ProcessedFile(file, size);
        }
    }

    private async Task<(bool NeedConversion, Size? Size)> GetVideoInfo(UploadedFile file, CancellationToken cancellationToken)
    {
        try {
            var media = await FFProbe.AnalyseAsync(file.TempFilePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            var video = media.PrimaryVideoStream;
            var size = video is null ? (Size?)null : new Size(video.Width, video.Height);
            var needsConversion = !OrdinalIgnoreCaseEquals(media.PrimaryVideoStream?.CodecName, "h264")
                && !OrdinalIgnoreCaseEquals(media.PrimaryVideoStream?.CodecName, VideoCodec.LibX264.Name);
            return (needsConversion, size);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to extract video info from '{FileName}'", file.FileName);
            return (false, null);
        }
    }
}
