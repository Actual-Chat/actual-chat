using ActualLab.IO;
using FFMpegCore;
using FFMpegCore.Enums;
namespace ActualChat.Uploads;

public sealed class LocalVideoUploadProcessor(ILogger<LocalVideoUploadProcessor> log) : IUploadProcessor
{
    private ILogger Log { get; } = log;

    public bool Supports(string contentType, MediaKind mediaKind)
        => MediaTypeExt.IsVideo(contentType);

    public async Task<ProcessedFile> Process(UploadedFile upload, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(0);
        var tempFile = await upload.DumpToTempFile(cancellationToken).ConfigureAwait(false);
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

        IMediaAnalysis? mediaInfo = null;
        try {
            mediaInfo = await FFProbe.AnalyseAsync(upload.TempFilePath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to extract video info from '{FileName}'", upload.FileName);
        }
        finally {
            Log.LogDebug("Video analysis completed in {Elapsed:N0}ms for '{FileName}'",
                stepSw.ElapsedMilliseconds,
                upload.FileName);
        }
        var videoStream = mediaInfo?.PrimaryVideoStream;
        if (videoStream is null)
            return new ProcessedFile(upload.AsBinaryFile(), null);

        var (size, duration, _) = UploadProcessorHelper.AnalyzeVideo(videoStream);
        var mustScale = UploadProcessorHelper.ExceedsFullHd(size);
        var mustConvert = mustScale
            || UploadProcessorHelper.MustConvertVideo(videoStream)
            || UploadProcessorHelper.MustConvertVideo(mediaInfo!.Format);

        progress?.Report(10);

        stepSw.Restart();
        var snapshot = await UploadProcessorHelper.Snapshot(upload.TempFilePath, upload.FileName, duration).ConfigureAwait(false);
        Log.LogDebug("Snapshot extraction completed in {Elapsed:N0}ms for '{FileName}'",
            stepSw.ElapsedMilliseconds, upload.FileName);
        if (snapshot is null)
            return new ProcessedFile(upload.AsBinaryFile(), size) { Duration = duration };

        progress?.Report(20);
        if (!mustConvert)
            return new ProcessedFile(UploadProcessorHelper.EnsureMp4Extension(upload), size, snapshot) { Duration = duration };

        try {
            stepSw.Restart();
            var tempDir = FilePath.GetApplicationTempDirectory();
            var convertedFileName = Guid.NewGuid().ToString("N") + "_" + FileExt.ShortenFileName(Path.ChangeExtension(upload.FileName, ".mp4"));
            var convertedFilePath = tempDir | convertedFileName;
            if (mustScale)
                size = UploadProcessorHelper.ScaleToFullHd(size);
            var ffMpegArguments = FFMpegArguments.FromFileInput(upload.TempFilePath)
                .OutputToFile(convertedFilePath,
                    false,
                    options => {
                        options.WithVideoCodec(VideoCodec.LibX264)
                            .WithFastStart()
                            .WithVariableBitrate(4);
                        if (mustScale)
                            options.WithVideoFilters(vf => vf.Scale(size.Width, size.Height));
                    });
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
            Log.LogDebug("Total video processing completed in {Elapsed:N0}ms for '{FileName}'",
                totalSw.ElapsedMilliseconds, upload.FileName);
            return new ProcessedFile(
                new UploadedTempFile(
                    Path.ChangeExtension(upload.FileName, ".mp4"),
                    "video/mp4",
                    convertedFilePath),
                size,
                snapshot) { Duration = duration };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            snapshot.Delete();
            throw;
        }
        catch (Exception e) {
            Log.LogError(e, "Could not convert uploaded video '{File}' after {Elapsed:N0}ms",
                upload.FileName, totalSw.ElapsedMilliseconds);
            return new ProcessedFile(upload, size, snapshot) { Duration = duration };
        }
    }
}
