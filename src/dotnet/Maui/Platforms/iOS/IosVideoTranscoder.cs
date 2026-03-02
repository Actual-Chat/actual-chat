using ActualChat.UI.App.Services;
using ActualLab.Diagnostics;
using ActualLab.IO;
using AVFoundation;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui;

public class IosVideoTranscoder(IServiceProvider services) : VideoTranscoder
{
    private const int MaxResolution = 1080;

    private ILogger Log => field ??= services.LogFor<IosVideoTranscoder>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.VideoTranscoding);

    public override async Task<FilePath> TranscodeIfNeeded(
        FilePath sourceFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        DebugLog?.LogInformation("TranscodeIfNeeded: '{Path}'", sourceFilePath);

        if (!await NeedsTranscoding(sourceFilePath).ConfigureAwait(false))
            return sourceFilePath;

        var sourceFileInfo = new FileInfo(sourceFilePath);
        Log.LogInformation("Transcoding video '{Path}' (size={Size})",
            sourceFilePath, sourceFileInfo.Length);

        var startedAt = CpuTimestamp.Now;
        var outputPath = await Transcode(sourceFilePath, progress, cancellationToken)
            .ConfigureAwait(false);

        if (outputPath == null)
            return sourceFilePath;

        var fileInfo = new FileInfo(outputPath.Value);
        Log.LogInformation("Transcoding completed: '{OutputPath}', size={Size} in {Elapsed}",
            outputPath, fileInfo.Length, startedAt.Elapsed.ToShortString());

        return outputPath;
    }

    private async Task<bool> NeedsTranscoding(FilePath filePath)
    {
        if (!OrdinalIgnoreCaseEquals(filePath.Extension, ".mp4")) {
            DebugLog?.LogInformation("NeedsTranscoding: true (extension '{Extension}' is not .mp4)", filePath.Extension);
            return true;
        }

        var resolution = await GetVideoResolution(filePath).ConfigureAwait(false);
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

    private async Task<FilePath> Transcode(
        FilePath sourcePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var sourceUrl = NSUrl.CreateFileUrl(sourcePath);
        var asset = new AVUrlAsset(sourceUrl);

        var exportSession = new AVAssetExportSession(asset, AVAssetExportSessionPreset.Preset1920x1080);
        FilePath outputDir = new FilePath(FileSystem.CacheDirectory) | "transcoded";
        Directory.CreateDirectory(outputDir);
        FilePath outputPath = outputDir | $"{sourcePath.FileNameWithoutExtension}.mp4";
        if (File.Exists(outputPath))
            outputPath = outputPath.ToUnique();
        var outputUrl = NSUrl.CreateFileUrl(outputPath);

        exportSession.OutputUrl = outputUrl;
        exportSession.OutputFileType = AVFileTypes.Mpeg4.GetConstant();
        exportSession.ShouldOptimizeForNetworkUse = true;

        DebugLog?.LogInformation(
            "Transcode: exporting '{Source}' -> '{Output}'", sourcePath, outputPath);

        try {
            // Start progress monitoring
            var progressWorker = FuncWorker.Start(
                ct => MonitorProgress(exportSession, progress, ct),
                cancellationToken);
            await using var _1 = progressWorker.ConfigureAwait(false);

            // Register cancellation
            await using var _ = cancellationToken.Register(() => exportSession.CancelExport()).ConfigureAwait(false);

            await exportSession.ExportTaskAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (exportSession.Status != AVAssetExportSessionStatus.Completed) {
                Log.LogWarning(
                    "AVAssetExportSession failed with status {Status}, error: {Error}",
                    exportSession.Status,
                    exportSession.Error?.LocalizedDescription ?? "unknown");
                CleanupFile(outputPath);
                return null;
            }

            progress?.Report(1.0);
            return outputPath;
        }
        catch (OperationCanceledException e) when(e.IsCancellationOf(cancellationToken)) {
            CleanupFile(outputPath);
            throw;
        }
        catch (Exception e) {
            Log.LogError(e, "Transcode: failed to export '{Source}'", sourcePath);
            CleanupFile(outputPath);
            return null;
        }
    }

    private async Task<CGSize?> GetVideoResolution(FilePath filePath)
    {
        try {
            DebugLog?.LogInformation("GetVideoResolution: '{FilePath}'", filePath);
            var url = NSUrl.CreateFileUrl(filePath);
            var asset = new AVUrlAsset(url);
            var tracks = await asset.LoadTracksWithMediaTypeAsync(AVMediaTypes.Video.GetConstant()!)
                .ConfigureAwait(false);
            if (tracks.Count == 0) {
                DebugLog?.LogInformation("GetVideoResolution: no video track found in '{FilePath}'", filePath);
                return null;
            }

            var track = tracks[0];
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
        catch (Exception e) {
            Log.LogError(e, "GetVideoResolution: failed to read video tracks from '{FilePath}'", filePath);
            return null;
        }
    }

    private async Task MonitorProgress(
        AVAssetExportSession session,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (progress == null)
            return;

        while (!cancellationToken.IsCancellationRequested) {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            progress.Report(session.Progress * 100);
            DebugLog?.LogDebug("Video transcoding progress: {Progress:P2}", session.Progress);
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
