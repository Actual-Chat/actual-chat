using System.Net.Mime;
using ActualLab.IO;
using FFMpegCore;
using MediaFormat = FFMpegCore.MediaFormat;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace ActualChat.Uploads;

public static class UploadProcessorHelper
{
    private static readonly ILogger Log = StaticLog.For(typeof(UploadProcessorHelper));
    private static readonly Size FullHd = new(1920, 1080);

    public static Size GetEffectiveSize(VideoStream video)
        => video.Rotation is 90 or 270 or -90 or -270
            ? new Size(video.Height, video.Width)
            : new Size(video.Width, video.Height);

    public static T EnsureMp4Extension<T>(T file) where T : UploadedFile
        => string.Equals(file.FileName.Extension, ".mp4", StringComparison.OrdinalIgnoreCase)
            ? file
            : file with { FileName = Path.ChangeExtension(file.FileName, ".mp4"), ContentType = "video/mp4" };

    public static (Size Size, TimeSpan Duration, double FrameRate) AnalyzeVideo(VideoStream videoStream)
    {
        var size = GetEffectiveSize(videoStream);
        return (size, videoStream.Duration, videoStream.AvgFrameRate);
    }

    public static bool MustConvertVideo(VideoStream videoStream)
    {
        var codecName = videoStream.CodecName;
        // Skip transcoding for H.264 and HEVC (H.265) codecs — a simple rename to .mp4 is enough
        return !string.Equals(codecName, "h264", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(codecName, "libx264", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(codecName, "hevc", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(codecName, "h265", StringComparison.OrdinalIgnoreCase);
    }

    public static bool MustConvertVideo(MediaFormat mediaFormat)
        => !IsMp4Container(mediaFormat);

    public static bool ExceedsFullHd(Size size)
    {
        var longSide = Math.Max(size.Width, size.Height);
        var shortSide = Math.Min(size.Width, size.Height);
        return longSide > FullHd.Width || shortSide > FullHd.Height;
    }

    public static Size ScaleToFullHd(Size size)
    {
        if (!ExceedsFullHd(size))
            return size;

        var isPortrait = size.Height > size.Width;
        var limit = isPortrait ? new Size(FullHd.Height, FullHd.Width) : FullHd;
        var scale = Math.Min((double)limit.Width / size.Width, (double)limit.Height / size.Height);
        // Round to even numbers (required by most video codecs)
        var w = (int)(size.Width * scale) & ~1;
        var h = (int)(size.Height * scale) & ~1;
        return new Size(w, h);
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

    public static async Task<UploadedTempFile> DumpToTempFile(UploadedFile file, CancellationToken cancellationToken)
    {
        var tempFileName = Guid.NewGuid() + "_" + file.FileName;
        var tempFilePath = FilePath.GetApplicationTempDirectory() & FileExt.ShortenFileName(tempFileName);
        var target = File.OpenWrite(tempFilePath);
        await using var _1 = target.ConfigureAwait(false);
        var source = await file.Open().ConfigureAwait(false);
        await using var _2 = source.ConfigureAwait(false);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        target.Position = 0;
        return new UploadedTempFile(file.FileName, file.ContentType, tempFilePath);
    }

    public static async Task<ProcessedFile> ProcessRasterImage(
        UploadedFile upload,
        int maxSize,
        bool convertToPng = false,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var inputStream = await upload.Open().ConfigureAwait(false);
        await using var _ = inputStream.ConfigureAwait(false);

        using var image = await Image.LoadAsync(inputStream, cancellationToken).ConfigureAwait(false);
        progress?.Report(30);

        var resizeRequired = image.Width > maxSize || image.Height > maxSize;
        var mustProcess = convertToPng || image.Metadata.ExifProfile != null || resizeRequired;
        if (!mustProcess)
            return new ProcessedFile(upload, image.Size);

        image.Mutate(img => {
            img.AutoOrient();
            if (resizeRequired)
                img.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(maxSize) });
        });
        image.Metadata.ExifProfile = null;
        var imageSize = image.Size;
        progress?.Report(60);

        var outPath = (FilePath.GetApplicationTempDirectory() & upload.FileName).ToUnique(randomLength: 10);
        if (convertToPng)
            outPath = outPath.ChangeExtension(".png");
        var outStream = File.OpenWrite(outPath);
        await using var _1 = outStream.ConfigureAwait(false);
        var format = convertToPng ? PngFormat.Instance : image.Metadata.DecodedImageFormat!;
        await image.SaveAsync(outStream, format, cancellationToken).ConfigureAwait(false);
        var contentType = convertToPng ? "image/png" : upload.ContentType;
        return new ProcessedFile(new UploadedTempFile(outPath.FileName, contentType, outPath), imageSize);
    }

    // Private helpers

    private static bool IsMp4Container(MediaFormat mediaFormat)
    {
        var tags = mediaFormat.Tags;
        if (tags is null)
            return false;

        if (!tags.TryGetValue("major_brand", out var brand1))
            return false;

        var brand = brand1.Trim();
        return brand is "isom" or "iso2" or "mp41" or "mp42";
    }

    private static async Task<UploadedTempFile?> SnapshotInternal(
        Func<TimeSpan, FFMpegArguments> createInput,
        FilePath fileName, TimeSpan totalVideoDuration)
    {
        if (totalVideoDuration <= TimeSpan.Zero)
            return null;

        try {
            var captureTime = (totalVideoDuration * 0.1).Clamp(TimeSpan.Zero, TimeSpan.FromSeconds(10));
            var snapshotPath = FilePath.GetApplicationTempDirectory() | $"snapshot_{Guid.NewGuid()}.jpg";
            var inputArgs = createInput(captureTime);
            await inputArgs
                .OutputToFile(snapshotPath, false, options => options.WithVideoCodec("mjpeg").WithFrameOutputCount(1))
                .ProcessAsynchronously()
                .ConfigureAwait(false);
            var snapshotFileName = fileName.ChangeExtension(".thumbnail.jpg");
            return new UploadedTempFile(snapshotFileName, MediaTypeNames.Image.Jpeg, snapshotPath);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to extract snapshot for '{FileName}'", fileName);
            return null;
        }
    }
}
