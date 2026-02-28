using ActualChat.UI.App.Services;
using ActualLab.Diagnostics;
using ActualLab.IO;
using AVFoundation;

namespace ActualChat.Maui;

public class IosVideoTranscoder(IServiceProvider services) : IVideoTranscoder
{
    private const int MaxResolution = 1080;

    private ILogger Log => field ??= services.LogFor<IosVideoTranscoder>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information);

    public async Task<VideoTranscodeResult?> TranscodeIfNeeded(
        FilePath sourceFilePath,
        string mimeType,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        DebugLog?.LogInformation(
            "TranscodeIfNeeded: '{Path}', mimeType={MimeType}",
            sourceFilePath, mimeType);

        if (!NeedsTranscoding(sourceFilePath, mimeType))
            return null;

        DebugLog?.LogInformation("Starting transcoding for '{Path}'", sourceFilePath);

        var outputPath = await Transcode(sourceFilePath, progress, cancellationToken)
            .ConfigureAwait(false);

        if (outputPath == null) {
            DebugLog?.LogInformation("Transcoding returned null for '{Path}'", sourceFilePath);
            return null;
        }

        var fileInfo = new FileInfo(outputPath.Value);
        DebugLog?.LogInformation(
            "Transcoding completed: '{OutputPath}', size={Size}",
            outputPath, fileInfo.Length);

        return new VideoTranscodeResult(outputPath.Value, "video/mp4", fileInfo.Length);
    }

    private bool NeedsTranscoding(FilePath filePath, string mimeType)
    {
        if (!OrdinalIgnoreCaseEquals(mimeType, "video/mp4")) {
            DebugLog?.LogInformation(
                "NeedsTranscoding: true (mimeType={MimeType} is not video/mp4)", mimeType);
            return true;
        }

        var resolution = GetVideoResolution(filePath);
        if (resolution == null) {
            DebugLog?.LogInformation("NeedsTranscoding: false (can't read resolution)");
            return false;
        }

        var minDimension = (int)Math.Min(resolution.Value.Width, resolution.Value.Height);
        var needs = minDimension > MaxResolution;
        DebugLog?.LogInformation(
            "NeedsTranscoding: {Result} (resolution={Width}x{Height}, minDimension={Min}, max={Max})",
            needs, (int)resolution.Value.Width, (int)resolution.Value.Height, minDimension, MaxResolution);
        return needs;
    }

    private async Task<FilePath?> Transcode(
        FilePath sourcePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var sourceUrl = NSUrl.CreateFileUrl(sourcePath);
        var asset = new AVUrlAsset(sourceUrl);

        var exportSession = new AVAssetExportSession(asset, AVAssetExportSessionPreset.Preset1920x1080);
        FilePath outputPath = new FilePath(Path.GetTempPath()) & $"{Guid.NewGuid():N}.mp4";
        var outputUrl = NSUrl.CreateFileUrl(outputPath);

        exportSession.OutputUrl = outputUrl;
        exportSession.OutputFileType = AVFileTypes.Mpeg4.GetConstant();
        exportSession.ShouldOptimizeForNetworkUse = true;

        DebugLog?.LogInformation(
            "Transcode: exporting '{Source}' -> '{Output}'", sourcePath, outputPath);

        // Start progress monitoring
        using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressTask = MonitorProgress(exportSession, progress, progressCts.Token);

        // Register cancellation
        await using var _ = cancellationToken.Register(() => exportSession.CancelExport()).ConfigureAwait(false);

        await exportSession.ExportTaskAsync().ConfigureAwait(false);

        // Stop progress monitoring
        await progressCts.CancelAsync().ConfigureAwait(false);
        await progressTask.ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested) {
            DebugLog?.LogInformation("Transcode: cancelled");
            CleanupFile(outputPath);
            throw new OperationCanceledException(cancellationToken);
        }

        if (exportSession.Status != AVAssetExportSessionStatus.Completed) {
            Log.LogWarning(
                "AVAssetExportSession failed with status {Status}, error: {Error}",
                exportSession.Status,
                exportSession.Error?.LocalizedDescription ?? "unknown");
            CleanupFile(outputPath);
            return null;
        }

        progress?.Report(1.0);
        DebugLog?.LogInformation("Transcode: export completed successfully");
        return outputPath;
    }

    private CGSize? GetVideoResolution(FilePath filePath)
    {
        var url = NSUrl.CreateFileUrl(filePath);
        var asset = new AVUrlAsset(url);
        var track = asset.TracksWithMediaType(AVMediaTypes.Video.GetConstant()!).FirstOrDefault();
        if (track == null) {
            DebugLog?.LogInformation("GetVideoResolution: no video track found");
            return null;
        }

        var size = track.NaturalSize;
        var transform = track.PreferredTransform;

        // Apply transform to handle rotated videos (e.g., portrait recordings)
        var isRotated = Math.Abs(transform.A) < 0.01 && Math.Abs(transform.D) < 0.01;
        var result = isRotated
            ? new CGSize(Math.Abs(size.Height), Math.Abs(size.Width))
            : new CGSize(Math.Abs(size.Width), Math.Abs(size.Height));
        DebugLog?.LogInformation(
            "GetVideoResolution: {Width}x{Height} (isRotated={IsRotated})",
            (int)result.Width, (int)result.Height, isRotated);
        return result;
    }

    private static async Task MonitorProgress(
        AVAssetExportSession session,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (progress == null)
            return;

        try {
            while (!cancellationToken.IsCancellationRequested) {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                progress.Report(session.Progress);
            }
        }
        catch (OperationCanceledException) {
            // Expected when export completes
        }
    }

    private static void CleanupFile(FilePath path)
    {
        try {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch {
            // Best effort cleanup
        }
    }
}
