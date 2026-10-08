using FFMpegCore;
namespace ActualChat.Uploads;

public sealed class LocalVideoUploadProcessor(ILogger<LocalVideoUploadProcessor> log) : IUploadProcessor
{
    private ILogger Log { get; } = log;

    public bool Supports(string contentType, MediaKind mediaKind)
        => MediaTypeExt.IsVideo(contentType);

    public async Task<ProcessedFile> Process(
        UploadedFile upload,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
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

    // Private methods

    private async Task<ProcessedFile> ProcessInternal(
        UploadedTempFile upload,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
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
        var conversion = UploadProcessorHelper.GetConversion(mediaInfo!);

        progress?.Report(10);

        stepSw.Restart();
        var snapshot = await UploadProcessorHelper
            .Snapshot(upload.TempFilePath, upload.FileName, duration)
            .ConfigureAwait(false);
        Log.LogDebug("Snapshot extraction completed in {Elapsed:N0}ms for '{FileName}'",
            stepSw.ElapsedMilliseconds, upload.FileName);

        progress?.Report(20);
        if (conversion is VideoConversion.None) {
            var renamed = UploadProcessorHelper.EnsureMp4Extension(upload);
            return new ProcessedFile(renamed, size, snapshot) { Duration = duration };
        }

        try {
            stepSw.Restart();
            var (convertedFile, convertedSize) = await UploadProcessorHelper
                .ConvertLocally(
                    () => FFMpegArguments.FromFileInput(upload.TempFilePath),
                    upload.FileName,
                    mediaInfo!,
                    conversion,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            Log.LogDebug("Local {Conversion} completed in {Elapsed:N0}ms for '{FileName}'",
                conversion, stepSw.ElapsedMilliseconds, upload.FileName);
            progress?.Report(98);
            Log.LogDebug("Total video processing completed in {Elapsed:N0}ms for '{FileName}'",
                totalSw.ElapsedMilliseconds, upload.FileName);
            return new ProcessedFile(convertedFile, convertedSize, snapshot) { Duration = duration };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            snapshot?.Delete();
            throw;
        }
        catch (Exception e) {
            Log.LogError(e, "Could not convert uploaded video '{File}' after {Elapsed:N0}ms",
                upload.FileName, totalSw.ElapsedMilliseconds);
            return new ProcessedFile(upload, size, snapshot) { Duration = duration };
        }
    }
}
