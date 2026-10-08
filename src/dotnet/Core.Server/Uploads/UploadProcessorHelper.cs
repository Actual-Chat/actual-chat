using System.Net.Mime;
using ActualLab.Generators;
using ActualLab.IO;
using FFMpegCore;
using FFMpegCore.Enums;
using MediaFormat = FFMpegCore.MediaFormat;
namespace ActualChat.Uploads;

public static class UploadProcessorHelper
{
    private const int TranscodeThreadCount = 2;

    private static readonly ILogger Log = StaticLog.For(typeof(UploadProcessorHelper));
    private static readonly Size2D FullHd = new(1920, 1080);
    // x264 takes every core it sees, so concurrent transcodes would starve the rest of the pod
    private static readonly SemaphoreSlim TranscodeLock = new(1, 1);

    public static VideoConversion GetConversion(IMediaAnalysis mediaInfo)
    {
        var videoStream = mediaInfo.PrimaryVideoStream!;
        if (ExceedsFullHd(GetEffectiveSize(videoStream)) || MustTranscode(videoStream))
            return VideoConversion.Transcode;

        return IsMp4Container(mediaInfo.Format) ? VideoConversion.None : VideoConversion.Remux;
    }

    public static async Task<(UploadedTempFile File, Size2D Size)> ConvertLocally(
        Func<FFMpegArguments> createInput,
        FilePath fileName,
        IMediaAnalysis mediaInfo,
        VideoConversion conversion,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (conversion is VideoConversion.Remux) {
            try {
                var remuxed = await Remux(createInput, fileName, mediaInfo, cancellationToken).ConfigureAwait(false);
                return (remuxed, GetEffectiveSize(mediaInfo.PrimaryVideoStream!));
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
                Log.LogWarning(e, "Failed to remux '{FileName}', transcoding it instead", fileName);
            }
        }
        return await Transcode(createInput, fileName, mediaInfo, progress, cancellationToken).ConfigureAwait(false);
    }

    public static T EnsureMp4Extension<T>(T file) where T : UploadedFile
        => string.Equals(file.FileName.Extension, ".mp4", StringComparison.OrdinalIgnoreCase)
            ? file
            : file with { FileName = file.FileName.ChangeExtension(".mp4"), ContentType = "video/mp4" };

    public static (Size2D Size, TimeSpan Duration, double FrameRate) AnalyzeVideo(IMediaAnalysis mediaInfo)
    {
        var videoStream = mediaInfo.PrimaryVideoStream!;
        var size = GetEffectiveSize(videoStream);
        // A stream without its own duration (e.g. a MediaRecorder WebM) still has the container's one
        var duration = videoStream.Duration > TimeSpan.Zero ? videoStream.Duration : mediaInfo.Format.Duration;
        return (size, duration, videoStream.AvgFrameRate);
    }

    public static Size2D ScaleToFullHd(Size2D size)
    {
        if (!ExceedsFullHd(size))
            return size;

        var isPortrait = size.Height > size.Width;
        var limit = isPortrait ? new Size2D(FullHd.Height, FullHd.Width) : FullHd;
        var scale = Math.Min((double)limit.Width / size.Width, (double)limit.Height / size.Height);
        // Round to even numbers (required by most video codecs)
        var w = (int)(size.Width * scale) & ~1;
        var h = (int)(size.Height * scale) & ~1;
        return new Size2D(w, h);
    }

    public static Task<UploadedTempFile?> Snapshot(
        Uri source, FilePath fileName, TimeSpan totalVideoDuration)
        => SnapshotInternal(
            at => FFMpegArguments.FromUrlInput(source, options => options.Seek(at)),
            fileName, totalVideoDuration);

    public static Task<UploadedTempFile?> Snapshot(
        FilePath source, FilePath fileName, TimeSpan totalVideoDuration)
        => SnapshotInternal(
            at => FFMpegArguments.FromFileInput(source, true, options => options.Seek(at)),
            fileName, totalVideoDuration);

    public static bool MustTranscode(VideoStream videoStream)
    {
        // H.264 and HEVC (H.265) frames are kept as they are: such a video needs at most a remux
        var codecName = videoStream.CodecName;
        return !string.Equals(codecName, "h264", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(codecName, "libx264", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(codecName, "hevc", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(codecName, "h265", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMp4Container(MediaFormat mediaFormat)
    {
        var tags = mediaFormat.Tags;
        if (tags is null)
            return false;

        if (!tags.TryGetValue("major_brand", out var brand1))
            return false;

        var brand = brand1.Trim();
        return brand is "isom" or "iso2" or "mp41" or "mp42";
    }

    public static bool ExceedsFullHd(Size2D size)
    {
        var longSide = Math.Max(size.Width, size.Height);
        var shortSide = Math.Min(size.Width, size.Height);
        return longSide > FullHd.Width || shortSide > FullHd.Height;
    }

    public static Size2D GetEffectiveSize(VideoStream video)
        => video.Rotation is 90 or 270 or -90 or -270
            ? new Size2D(video.Height, video.Width)
            : new Size2D(video.Width, video.Height);

    // Private methods

    private static async Task<UploadedTempFile> Remux(
        Func<FFMpegArguments> createInput,
        FilePath fileName,
        IMediaAnalysis mediaInfo,
        CancellationToken cancellationToken)
    {
        // Safari plays HEVC in MP4 only when it's tagged hvc1
        var isHevc = mediaInfo.PrimaryVideoStream!.CodecName is "hevc" or "h265";
        var outputPath = NewConvertedFilePath(fileName);
        try {
            await createInput()
                .OutputToFile(outputPath, true, options => {
                    options.CopyChannel().WithFastStart();
                    if (isHevc)
                        options.WithCustomArgument("-tag:v hvc1");
                })
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously()
                .ConfigureAwait(false);
        }
        catch {
            File.Delete(outputPath);
            throw;
        }

        return new UploadedTempFile(fileName.ChangeExtension(".mp4"), "video/mp4", outputPath);
    }

    private static async Task<(UploadedTempFile File, Size2D Size)> Transcode(
        Func<FFMpegArguments> createInput,
        FilePath fileName,
        IMediaAnalysis mediaInfo,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var (size, duration, _) = AnalyzeVideo(mediaInfo);
        var mustScale = ExceedsFullHd(size);
        if (mustScale)
            size = ScaleToFullHd(size);
        var mustCopyAudio = mediaInfo.PrimaryAudioStream is null or { CodecName: "aac" };
        var outputPath = NewConvertedFilePath(fileName);
        var arguments = createInput()
            .OutputToFile(outputPath, true, options => {
                // yuv420p: AV1 and HEVC sources are often 10-bit, and browsers don't play 10-bit H.264
                options.WithVideoCodec(VideoCodec.LibX264)
                    .WithSpeedPreset(Speed.VeryFast)
                    .ForcePixelFormat("yuv420p")
                    .UsingThreads(TranscodeThreadCount)
                    .WithFastStart();
                if (mustCopyAudio)
                    options.CopyChannel(FFMpegCore.Enums.Channel.Audio);
                else
                    options.WithAudioCodec(AudioCodec.Aac);
                if (mustScale)
                    options.WithVideoFilters(vf => vf.Scale(size.Width, size.Height));
            })
            .CancellableThrough(cancellationToken);
        // Percentages are computed against the duration, so a video without one reports none
        if (progress is not null && duration > TimeSpan.Zero)
            arguments = arguments.NotifyOnProgress(p => progress.Report(20 + (0.78 * p)), duration);

        await TranscodeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await arguments.ProcessAsynchronously().ConfigureAwait(false);
        }
        catch {
            File.Delete(outputPath);
            throw;
        }
        finally {
            TranscodeLock.Release();
        }

        var file = new UploadedTempFile(fileName.ChangeExtension(".mp4"), "video/mp4", outputPath);
        return (file, size);
    }

    private static async Task<UploadedTempFile?> SnapshotInternal(
        Func<TimeSpan, FFMpegArguments> createInput,
        FilePath fileName, TimeSpan totalVideoDuration)
    {
        // The first frame is the fallback: a MediaRecorder WebM reports no duration,
        // and in a truncated file the seek can land past the last frame
        var captureTime = (totalVideoDuration * 0.1).Clamp(TimeSpan.Zero, TimeSpan.FromSeconds(10));
        if (captureTime > TimeSpan.Zero) {
            var snapshot = await TrySnapshot(createInput, fileName, captureTime).ConfigureAwait(false);
            if (snapshot is not null)
                return snapshot;
        }

        var firstFrame = await TrySnapshot(createInput, fileName, TimeSpan.Zero).ConfigureAwait(false);
        if (firstFrame is null)
            Log.LogError("Failed to extract snapshot for '{FileName}'", fileName);
        return firstFrame;
    }

    private static async Task<UploadedTempFile?> TrySnapshot(
        Func<TimeSpan, FFMpegArguments> createInput,
        FilePath fileName,
        TimeSpan captureTime)
    {
        var snapshotId = RandomStringGenerator.Default.Next();
        var snapshotPath = FilePath.GetApplicationTempDirectory() | $"snapshot_{snapshotId}.jpg";
        try {
            await createInput(captureTime)
                .OutputToFile(snapshotPath, false, options => options.WithVideoCodec("mjpeg").WithFrameOutputCount(1))
                .ProcessAsynchronously()
                .ConfigureAwait(false);
            // ffmpeg exits cleanly with no output when it decodes no frame after the seek point
            var snapshotFile = new FileInfo(snapshotPath);
            if (snapshotFile is not { Exists: true, Length: > 0 }) {
                Log.LogWarning("No frame at {CaptureTime} for a snapshot of '{FileName}'", captureTime, fileName);
                File.Delete(snapshotPath);
                return null;
            }

            var snapshotFileName = fileName.ChangeExtension(".thumbnail.jpg");
            return new UploadedTempFile(snapshotFileName, MediaTypeNames.Image.Jpeg, snapshotPath);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to extract snapshot at {CaptureTime} for '{FileName}'", captureTime, fileName);
            File.Delete(snapshotPath);
            return null;
        }
    }

    private static FilePath NewConvertedFilePath(FilePath fileName)
    {
        var convertedFileName = FileExt.ShortenFileName(fileName.ChangeExtension(".mp4"));
        return FilePath.GetApplicationTempDirectory() | $"{RandomStringGenerator.Default.Next()}_{convertedFileName}";
    }
}
