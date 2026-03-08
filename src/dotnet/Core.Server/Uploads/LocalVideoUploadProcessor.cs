using System.Net.Mime;
using ActualChat.Media;
using ActualLab.IO;
using FFMpegCore;
using FFMpegCore.Enums;
using SixLabors.ImageSharp;

namespace ActualChat.Uploads;

public class LocalVideoUploadProcessor(ILogger<LocalVideoUploadProcessor> log) : IUploadProcessor
{
    private ILogger Log { get; } = log;

    public bool Supports(string contentType)
        => MediaTypeExt.IsVideo(contentType);

    public async Task<ProcessedFile> Process(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(0);
        var tempFile = await UploadHelper.DumpToTempFile(upload, cancellationToken).ConfigureAwait(false);
        ProcessedFile processedFile;
        try {
            processedFile = await ProcessInternal(tempFile, progress, cancellationToken).ConfigureAwait(false);
        }
        catch {
            tempFile.Delete();
            throw;
        }
        if (processedFile.File != tempFile)
            tempFile.Delete();
        return processedFile;
    }

    private async Task<ProcessedFile> ProcessInternal(UploadedTempFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();

        var (mustConvert, size, duration) = await GetVideoInfo(upload, upload.TempFilePath, cancellationToken).ConfigureAwait(false);
        Log.LogDebug("Video analysis completed in {Elapsed:N0}ms for '{FileName}'",
            stepSw.ElapsedMilliseconds, upload.FileName);
        if (size is null)
            return new ProcessedFile(upload.AsBinaryFile(), null);

        progress?.Report(10);

        stepSw.Restart();
        var thumbnail = await GetThumbnail(upload, upload.TempFilePath, duration).ConfigureAwait(false);
        Log.LogDebug("Thumbnail extraction completed in {Elapsed:N0}ms for '{FileName}'",
            stepSw.ElapsedMilliseconds, upload.FileName);
        if (thumbnail is null)
            return new ProcessedFile(upload.AsBinaryFile(), size);

        progress?.Report(20);
        if (!mustConvert)
            return new ProcessedFile(upload, size, thumbnail);

        try {
            stepSw.Restart();
            var tempDir = FilePath.GetApplicationTempDirectory();
            var convertedFileName = Guid.NewGuid().ToString("N") + "_" + FileExt.ShortenFileName(Path.ChangeExtension(upload.FileName, ".mp4"));
            var convertedFilePath = tempDir | convertedFileName;
            var ffMpegArguments = FFMpegArguments.FromFileInput(upload.TempFilePath)
                .OutputToFile(convertedFilePath,
                    false,
                    options => options.WithVideoCodec(VideoCodec.LibX264)
                        .WithFastStart()
                        .WithVariableBitrate(4));
            if (progress is not null) {
                // Progress from 20% to 98% during conversion
                Action<double> onPercentageProgress = p => {
                    var reportProgress = 20 + (0.78 * p);
                    progress.Report(reportProgress);
                };
                ffMpegArguments = ffMpegArguments.NotifyOnProgress(onPercentageProgress, duration);
            }
            await ffMpegArguments
                .ProcessAsynchronously()
                .ConfigureAwait(false);
            Log.LogDebug("Local transcoding completed in {Elapsed:N0}ms for '{FileName}'",
                stepSw.ElapsedMilliseconds, upload.FileName);
            progress?.Report(98);
            // Delete an original temp file since we have a new converted file
            upload.Delete();

            Log.LogDebug("Total video processing completed in {Elapsed:N0}ms for '{FileName}'",
                totalSw.ElapsedMilliseconds, upload.FileName);
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
            Log.LogError(e, "Could not convert uploaded video '{File}' after {Elapsed:N0}ms",
                upload.FileName, totalSw.ElapsedMilliseconds);
            return new ProcessedFile(upload, size, thumbnail);
        }
    }

    private async Task<(bool MustConvert, Size? Size, TimeSpan Duration)> GetVideoInfo(UploadedFile videoUpload, FilePath videoTempFile, CancellationToken cancellationToken)
    {
        try {
            var media = await FFProbe.AnalyseAsync(videoTempFile, cancellationToken: cancellationToken).ConfigureAwait(false);
            var video = media.PrimaryVideoStream;
            var size = video is null ? (Size?)null : UploadHelper.GetEffectiveSize(video);
            return (UploadHelper.MustConvertVideo(media, videoUpload.FileName), size, media.Duration);
        }
        catch (Exception e) {
            Log.LogDebug(e, "Failed to extract video info from '{FileName}'", videoUpload.FileName);
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
