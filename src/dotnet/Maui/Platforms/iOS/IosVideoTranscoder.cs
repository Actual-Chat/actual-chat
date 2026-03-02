using ActualChat.UI.App.Services;
using ActualLab.Diagnostics;
using ActualLab.IO;
using AVFoundation;
using CoreMedia;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui;

public class IosVideoTranscoder(IServiceProvider services) : VideoTranscoder
{
    private const int MaxLongSide = 1920;
    private const int MaxShortSide = 1080;
    private const int MaxBitrate = 8_000_000; // 8 Mbps - HEVC 1080p typically outputs around this

    private ILogger Log => field ??= services.LogFor<IosVideoTranscoder>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.VideoTranscoding);

    protected override async Task<FilePath> TranscodeInternal(
        FilePath sourceFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        DebugLog?.LogInformation("TranscodeInternal: '{Path}'", sourceFilePath);

        var sourceFileInfo = new FileInfo(sourceFilePath);
        if (!await NeedsTranscoding(sourceFilePath, sourceFileInfo.Length).ConfigureAwait(false))
            return FilePath.Empty;

        Log.LogInformation("Transcoding video '{Path}' (size={Size})",
            sourceFilePath, sourceFileInfo.Length);

        var startedAt = CpuTimestamp.Now;
        var transcodedPath = await Transcode(sourceFilePath, progress, cancellationToken)
            .ConfigureAwait(false);

        if (transcodedPath.IsEmpty)
            return FilePath.Empty;

        var transcodedFileInfo = new FileInfo(transcodedPath);
        Log.LogInformation("Transcoding completed: '{OutputPath}', size={Size} ({Ratio:P0} of source) in {Elapsed}",
            transcodedPath, transcodedFileInfo.Length,
            (double)transcodedFileInfo.Length / sourceFileInfo.Length,
            startedAt.Elapsed.ToShortString());

        return transcodedPath;
    }

    private async Task<bool> NeedsTranscoding(FilePath filePath, long sourceSize)
    {
        if (!OrdinalIgnoreCaseEquals(filePath.Extension, ".mp4")) {
            Log.LogInformation("NeedsTranscoding: true (extension '{Extension}' is not .mp4)", filePath.Extension);
            return true;
        }

        var info = await GetVideoInfo(filePath).ConfigureAwait(false);
        if (info == null) {
            Log.LogInformation("NeedsTranscoding: false (can't read video info)");
            return false;
        }

        var (resolution, bitrate, duration, codec) = info.Value;

        // Skip transcoding if already HEVC - re-encoding HEVC to HEVC rarely helps
        if (codec == CMVideoCodecType.Hevc) {
            Log.LogInformation(
                "NeedsTranscoding: false (already HEVC, {Width}x{Height}, bitrate={Bitrate})",
                (int)resolution.Width, (int)resolution.Height, bitrate);
            return false;
        }

        var needsResize = resolution.LongSide > MaxLongSide || resolution.ShortSide > MaxShortSide;
        var needsCompress = bitrate > MaxBitrate;

        if (!needsResize && !needsCompress) {
            Log.LogInformation(
                "NeedsTranscoding: false ({Width}x{Height}, bitrate={Bitrate}, codec={Codec})",
                (int)resolution.Width, (int)resolution.Height, bitrate, codec);
            return false;
        }

        // Estimate output size: duration * target bitrate (8 Mbps for HEVC 1080p)
        var estimatedOutputSize = (long)(duration * MaxBitrate / 8);
        if (estimatedOutputSize >= sourceSize) {
            Log.LogInformation(
                "NeedsTranscoding: false (estimated {EstimatedSize} >= source {SourceSize})",
                estimatedOutputSize, sourceSize);
            return false;
        }

        Log.LogInformation(
            "NeedsTranscoding: true ({Width}x{Height}, bitrate={Bitrate}, codec={Codec}, estimated={EstimatedSize}, source={SourceSize})",
            (int)resolution.Width, (int)resolution.Height, bitrate, codec, estimatedOutputSize, sourceSize);
        return true;
    }

    private async Task<FilePath> Transcode(
        FilePath sourcePath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var sourceUrl = NSUrl.CreateFileUrl(sourcePath);
        var asset = new AVUrlAsset(sourceUrl);

        var exportSession = new AVAssetExportSession(asset, AVAssetExportSessionPreset.Hevc1920x1080);
        FilePath outputDir = new FilePath(FileSystem.CacheDirectory) | "transcoded";
        Directory.CreateDirectory(outputDir);
        FilePath outputPath = outputDir | $"{sourcePath.FileNameWithoutExtension}.mp4";
        if (File.Exists(outputPath))
            outputPath = outputPath.ToUnique();
        var outputUrl = NSUrl.CreateFileUrl(outputPath);

        exportSession.OutputUrl = outputUrl;
        exportSession.OutputFileType = AVFileTypes.Mpeg4.GetConstant();
        exportSession.ShouldOptimizeForNetworkUse = true;

        DebugLog?.LogInformation("Transcode: exporting '{Source}' -> '{Output}'", sourcePath, outputPath);

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
                return FilePath.Empty;
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

    private async Task<(CGSize Resolution, float Bitrate, double Duration, CMVideoCodecType Codec)?> GetVideoInfo(FilePath filePath)
    {
        try {
            DebugLog?.LogInformation("GetVideoInfo: '{FilePath}'", filePath);
            var url = NSUrl.CreateFileUrl(filePath);
            var asset = new AVUrlAsset(url);
            var tracks = await asset.LoadTracksWithMediaTypeAsync(AVMediaTypes.Video.GetConstant()!)
                .ConfigureAwait(false);
            if (tracks.Count == 0) {
                DebugLog?.LogInformation("GetVideoInfo: no video track found in '{FilePath}'", filePath);
                return null;
            }

            var track = tracks[0];
            var resolution = track.ActualResolution;
            var bitrate = track.EstimatedDataRate;
            var duration = asset.Duration.Seconds;
            var codec = track.VideoCodecType;
            DebugLog?.LogInformation(
                "GetVideoInfo: {Width}x{Height}, bitrate={Bitrate}, duration={Duration}s, codec={Codec}",
                (int)resolution.Width, (int)resolution.Height, bitrate, duration, codec);
            return (resolution, bitrate, duration, codec);
        }
        catch (Exception e) {
            Log.LogError(e, "GetVideoInfo: failed to read video tracks from '{FilePath}'", filePath);
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
