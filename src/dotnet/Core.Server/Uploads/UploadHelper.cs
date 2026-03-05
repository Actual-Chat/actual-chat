using ActualLab.IO;
using FFMpegCore;
using FFMpegCore.Enums;
using SixLabors.ImageSharp;

namespace ActualChat.Uploads;

public static class UploadHelper
{
    public static Size GetEffectiveSize(VideoStream video)
        => video.Rotation is 90 or 270 or -90 or -270
            ? new Size(video.Height, video.Width)
            : new Size(video.Width, video.Height);

    public static bool MustConvertVideo(IMediaAnalysis media, FilePath fileName)
    {
        if (!OrdinalIgnoreCaseEquals(fileName.Extension, ".mp4"))
            return true;

        var codecName = media.PrimaryVideoStream?.CodecName;
        // Skip transcoding for H.264 and HEVC (H.265) codecs
        return !OrdinalIgnoreCaseEquals(codecName, "h264")
            && !OrdinalIgnoreCaseEquals(codecName, VideoCodec.LibX264.Name)
            && !OrdinalIgnoreCaseEquals(codecName, "hevc")
            && !OrdinalIgnoreCaseEquals(codecName, "h265");
    }

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
}
