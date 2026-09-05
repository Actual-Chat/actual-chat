using ActualChat.UI.App.Services;
using ActualLab.Diagnostics;
using ActualLab.IO;
using AVFoundation;
using CoreMedia;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui;

public class AppleVideoTranscoder(IServiceProvider services) : VideoTranscoder
{
    private const int MaxLongSide = 1920;
    private const int MaxShortSide = 1080;
    private const int MaxBitrate = 8_000_000; // 8 Mbps - HEVC 1080p typically outputs around this
    private const long MaxRemuxSize = 70L * 1024 * 1024; // 70 MB - remux instead of transcode for small videos

    private ILogger Log => field ??= services.LogFor<AppleVideoTranscoder>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.VideoTranscoding);

    protected override async Task<FilePath> TranscodeInternal(
        FilePath sourceFilePath,
        IProgress<double> progress,
        CancellationToken cancellationToken = default)
    {
        DebugLog?.LogInformation("TranscodeInternal: '{Path}'", sourceFilePath);

        var sourceFileInfo = new FileInfo(sourceFilePath);
        var sourceSize = sourceFileInfo.Length;
        if (!await NeedsProcessing(sourceFilePath, sourceSize).ConfigureAwait(false))
            return FilePath.Empty;

        var remuxOnly = sourceSize <= MaxRemuxSize;
        Log.LogInformation(
            remuxOnly
                ? "Remuxing video '{Path}' (size={Size})"
                : "Transcoding video '{Path}' (size={Size})",
            sourceFilePath, sourceSize);
        var startedAt = CpuTimestamp.Now;
        var transcodedPath = await TranscodeVideo(sourceFilePath, remuxOnly, progress, cancellationToken).ConfigureAwait(false);
        if (transcodedPath.IsEmpty)
            return FilePath.Empty;

        var transcodedFileInfo = new FileInfo(transcodedPath);
        Log.LogInformation(
            remuxOnly
                ? "Remuxing completed: '{OutputPath}', size={Size} ({Ratio:P0} of source) in {Elapsed}"
                : "Transcoding completed: '{OutputPath}', size={Size} ({Ratio:P0} of source) in {Elapsed}",
            transcodedPath, transcodedFileInfo.Length,
            (double)transcodedFileInfo.Length / sourceSize,
            startedAt.Elapsed.ToShortString());

        return transcodedPath;
    }

    private async Task<bool> NeedsProcessing(FilePath filePath, long sourceSize)
    {
        var isMp4 = filePath.HasExtension(".mp4");

        // Small videos: remux to MP4 only if not already MP4
        if (sourceSize <= MaxRemuxSize) {
            if (!isMp4) {
                Log.LogInformation(
                    "NeedsProcessing: true, will remux (extension '{Extension}' is not .mp4, size={Size} <= {MaxSize})",
                    filePath.Extension, sourceSize, MaxRemuxSize);
                return true;
            }
            Log.LogInformation(
                "NeedsProcessing: false (already .mp4, size={Size} <= {MaxSize})",
                sourceSize, MaxRemuxSize);
            return false;
        }

        // Large videos: full transcoding checks
        if (!isMp4) {
            Log.LogInformation("NeedsProcessing: true (extension '{Extension}' is not .mp4)", filePath.Extension);
            return true;
        }

        var info = await GetVideoInfo(filePath).ConfigureAwait(false);
        if (info == null) {
            Log.LogInformation("NeedsProcessing: false (can't read video info)");
            return false;
        }

        var (resolution, bitrate, codec) = info.Value;
        if (codec is not CMVideoCodecType.Hevc and not CMVideoCodecType.H264) {
            Log.LogInformation("NeedsProcessing: true (codec '{Codec}' is not HEVC or H264)", codec);
            return true;
        }

        var needsResize = resolution.LongSide > MaxLongSide || resolution.ShortSide > MaxShortSide;
        var needsCompress = bitrate > MaxBitrate;

        if (!needsResize && !needsCompress) {
            Log.LogInformation(
                "NeedsProcessing: false ({Width}x{Height}, bitrate={Bitrate}, codec={Codec})",
                (int)resolution.Width, (int)resolution.Height, bitrate, codec);
            return false;
        }

        // Check estimated output size
        using var dExportSession = CreateExportSession(filePath);
        var estimatedSize = await dExportSession.Resource.EstimateOutputFileLengthAsync().ConfigureAwait(false);
        if (estimatedSize >= sourceSize) {
            Log.LogInformation(
                "NeedsProcessing: false (estimated {EstimatedSize} >= source {SourceSize})",
                estimatedSize, sourceSize);
            return false;
        }

        Log.LogInformation(
            "NeedsProcessing: true ({Width}x{Height}, bitrate={Bitrate}, codec={Codec}, estimated={EstimatedSize}, source={SourceSize})",
            (int)resolution.Width, (int)resolution.Height, bitrate, codec, estimatedSize, sourceSize);
        return true;
    }

    private async Task<FilePath> TranscodeVideo(
        FilePath sourcePath,
        bool remuxOnly,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        var outputPath = GetOutputPath(sourcePath);
        using var dExportSession = CreateExportSession(sourcePath, remuxOnly);
        var exportSession = dExportSession.Resource;
        using var outputUrl = NSUrl.CreateFileUrl(outputPath);
        exportSession.OutputUrl = outputUrl;

        DebugLog?.LogInformation(
            "Transcode: exporting '{Source}' -> '{Output}' (source={SourceSize})",
            sourcePath, outputPath, new FileInfo(sourcePath).Length);

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

            progress.Report(100);
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

    private static FilePath GetOutputPath(FilePath sourcePath)
    {
        FilePath outputDir = new FilePath(FileSystem.CacheDirectory) | "transcoded";
        Directory.CreateDirectory(outputDir);
        FilePath outputPath = outputDir | $"{sourcePath.FileNameWithoutExtension}.mp4";
        return outputPath.ToUnique();
    }

    private async Task<(CGSize Resolution, float Bitrate, CMVideoCodecType Codec)?> GetVideoInfo(FilePath filePath)
    {
        try {
            DebugLog?.LogInformation("GetVideoInfo: '{FilePath}'", filePath);
            using var url = NSUrl.CreateFileUrl(filePath);
            using var asset = new AVUrlAsset(url);
            var tracks = await asset.LoadTracksWithMediaTypeAsync(AVMediaTypes.Video.GetConstant()!)
                .ConfigureAwait(false);
            if (tracks.Count == 0) {
                DebugLog?.LogInformation("GetVideoInfo: no video track found in '{FilePath}'", filePath);
                return null;
            }

            var track = tracks[0]!;
            var resolution = track.ActualResolution;
            var bitrate = track.EstimatedDataRate;
            var codec = track.VideoCodecType;
            DebugLog?.LogInformation(
                "GetVideoInfo: {Width}x{Height}, bitrate={Bitrate}, codec={Codec}",
                (int)resolution.Width, (int)resolution.Height, bitrate, codec);
            return (resolution, bitrate, codec);
        }
        catch (Exception e) {
            Log.LogError(e, "GetVideoInfo: failed to read video tracks from '{FilePath}'", filePath);
            return null;
        }
    }

    private async Task MonitorProgress(
        AVAssetExportSession session,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested) {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            progress.Report(session.Progress * 100);
            DebugLog?.LogDebug("Video transcoding progress: {Progress:P2}", session.Progress);
        }
    }

    private static Disposable<AVAssetExportSession> CreateExportSession(FilePath sourcePath, bool remuxOnly = false)
    {
        var sourceUrl = NSUrl.CreateFileUrl(sourcePath);
        var asset = new AVUrlAsset(sourceUrl);
        var preset = remuxOnly
            ? AVAssetExportSessionPreset.Passthrough
            : AVAssetExportSessionPreset.Hevc1920x1080;
        var session = new AVAssetExportSession(asset, preset);
        session.OutputFileType = AVFileTypes.Mpeg4.GetConstant();
        session.ShouldOptimizeForNetworkUse = true;
        return Disposable.New(session, _ => {
            session.DisposeSilently();
            asset.DisposeSilently();
            sourceUrl.DisposeSilently();
        });
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
