using System.Net.Mime;
using ActualChat.Media;
using FFMpegCore;
using FFMpegCore.Arguments;
using FFMpegCore.Enums;
using ActualLab.IO;

namespace ActualChat.Uploads;

public class VideoUploadProcessor(ILogger<VideoUploadProcessor> log) : IUploadProcessor
{
    private ILogger<VideoUploadProcessor> Log { get; } = log;

    public bool Supports(string contentType)
        => MediaTypeExt.IsVideo(contentType);

    public async Task<ProcessedFile> Process(UploadedTempFile upload, CancellationToken cancellationToken)
    {
        var (mustConvert, size, duration) = await GetVideoInfo(upload, upload.TempFilePath, cancellationToken).ConfigureAwait(false);
        if (size is null)
            // we consider it as a file not as a video
            return new ProcessedFile(upload with { ContentType = MediaTypeNames.Application.Octet }, null);

        var thumbnail = await GetThumbnail(upload, upload.TempFilePath, duration).ConfigureAwait(false);
        if (thumbnail is null)
            // we consider it as a file not as a video
            return new ProcessedFile(upload with { ContentType = MediaTypeNames.Application.Octet }, null);

        if (!mustConvert)
            return new ProcessedFile(upload, size, thumbnail);

        try {
            var tempDir = FilePath.GetApplicationTempDirectory();
            var convertedFileName = Guid.NewGuid().ToString("N") + "_" + Path.ChangeExtension(upload.FileName, ".mp4");
            var convertedFilePath = tempDir | convertedFileName;
            await FFMpegArguments.FromFileInput(upload.TempFilePath)
                .OutputToFile(convertedFilePath,
                    false,
                    options => options.WithVideoCodec(VideoCodec.LibX264)
                        .WithFastStart()
                        .WithVariableBitrate(4)
                        .WithArgument(new VideoFiltersArgument(new VideoFilterOptions())))
                .ProcessAsynchronously()
                .ConfigureAwait(false);
            return new ProcessedFile(
                new UploadedTempFile(
                    Path.ChangeExtension(upload.FileName, ".mp4"),
                    "video/mp4",
                    convertedFilePath),
                size,
                thumbnail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            thumbnail?.Delete();
            throw;
        }
        catch (Exception e) {
            Log.LogError(e, "Could not convert uploaded video '{File}'", upload.FileName);
            return new ProcessedFile(upload, size, thumbnail);
        }
    }

    private async Task<(bool MustConvert, Size? Size, TimeSpan Duration)> GetVideoInfo(UploadedFile videoUpload, FilePath videoTempFile, CancellationToken cancellationToken)
    {
        try {
            var media = await FFProbe.AnalyseAsync(videoTempFile, cancellationToken: cancellationToken).ConfigureAwait(false);
            var video = media.PrimaryVideoStream;
            var size = video is null ? (Size?)null : new Size(video.Width, video.Height);
            var mustConvert = !OrdinalIgnoreCaseEquals(media.PrimaryVideoStream?.CodecName, "h264")
                && !OrdinalIgnoreCaseEquals(media.PrimaryVideoStream?.CodecName, VideoCodec.LibX264.Name);
            return (mustConvert, size, media.Duration);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to extract video info from '{FileName}'", videoUpload.FileName);
            return (false, null, TimeSpan.Zero);
        }
    }

    private async Task<UploadedTempFile?> GetThumbnail(UploadedFile videoUpload, FilePath videoTempFile, TimeSpan totalVideoDuration)
    {
        if (totalVideoDuration <= TimeSpan.Zero)
            return null;

        try {
            var at = (totalVideoDuration * 0.1).Clamp(TimeSpan.Zero, TimeSpan.FromSeconds(10));
            var thumbnailPath = FilePath.GetApplicationTempDirectory() | $"snapshot_{Guid.NewGuid()}.jpg";
            var success = await FFMpeg.SnapshotAsync(videoTempFile, thumbnailPath, captureTime: at).ConfigureAwait(false);
            if (!success)
                throw StandardError.External($"Could not take thumbnail for video {videoUpload.FileName}.");

            await FFMpegArguments.FromFileInput(videoTempFile, true, options => options.Seek(at))
                .OutputToFile(thumbnailPath, false, options => options.WithVideoCodec("mjpeg").WithFrameOutputCount(1))
                .ProcessAsynchronously()
                .ConfigureAwait(false);

            var thumbnailFileName = videoUpload.FileName.ChangeExtension(".thumbnail.jpg");
            return new UploadedTempFile(thumbnailFileName, MediaTypeNames.Image.Jpeg, thumbnailPath);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to extract thumbnail for '{FileName}'", videoUpload.FileName);
            return null;
        }
    }
}
