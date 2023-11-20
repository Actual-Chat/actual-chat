using System.Net.Mime;
using ActualChat.Media;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using Stl.IO;

namespace ActualChat.Uploads;

public class VideoUploadProcessor(ILogger<VideoUploadProcessor> log) : IUploadProcessor
{
    private ILogger<VideoUploadProcessor> Log { get; } = log;

    public bool Supports(string contentType)
        => MediaTypeExt.IsVideo(contentType);

    public async Task<ProcessedFile> Process(UploadedFile file, CancellationToken cancellationToken)
    {
        var (needConversion, size) = await GetVideoInfo(file, cancellationToken).ConfigureAwait(false);
        if (size == null)
            file = file with { ContentType = MediaTypeNames.Application.Octet }; // further we think that it's not a video
        if (!needConversion)
            return new ProcessedFile(file, size);

        try {
            var tempDir = FilePath.GetApplicationTempDirectory();
            var convertedFileName = Guid.NewGuid().ToString("N") + "_" + Path.ChangeExtension(file.FileName, ".mp4");
            var convertedFilePath = tempDir | convertedFileName;
            var stream = await file.Open().ConfigureAwait(false);
            await using var _ = stream.ConfigureAwait(false);
            await FFMpegArguments.FromPipeInput(new StreamPipeSource(stream))
                .OutputToFile(convertedFilePath,
                    false,
                    options => options.WithVideoCodec(VideoCodec.LibX264)
                        .WithFastStart()
                        .WithVariableBitrate(4))
                .ProcessAsynchronously()
                .ConfigureAwait(false);
            return new ProcessedFile(
                new UploadedTempFile(
                    Path.ChangeExtension(file.FileName, ".mp4"),
                    "video/mp4",
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
            var stream = await file.Open().ConfigureAwait(false);
            await using var _ = stream.ConfigureAwait(false);
            var media = await FFProbe.AnalyseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
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
