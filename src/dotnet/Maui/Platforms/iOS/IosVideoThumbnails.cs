using ActualChat.UI.App;
using ActualLab.IO;
using AVFoundation;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui;

public sealed record VideoThumbnail(FilePath Path, Size Size);

public class IosVideoThumbnails(IServiceProvider services)
{
    private static readonly FilePath CacheDir = new FilePath(FileSystem.CacheDirectory) | "video-thumbnails";

    private ILogger Log => field ??= services.LogFor<IosVideoThumbnails>();

    public async Task<VideoThumbnail?> Generate(FilePath videoPath, CancellationToken cancellationToken = default)
    {
        try {
            // Create a stable thumbnail path based on the source file name
            var thumbnailFileName = videoPath.FileName.ChangeExtension(".jpg");
            Directory.CreateDirectory(CacheDir);
            var thumbnailPath = CacheDir | thumbnailFileName;

            // Return cached thumbnail if it exists
            if (File.Exists(thumbnailPath)) {
                Log.LogInformation("Generate: using cached thumbnail '{ThumbnailPath}'", thumbnailPath);
                var size = await GetImageSize(thumbnailPath).ConfigureAwait(false);
                return new VideoThumbnail(thumbnailPath, size);
            }

            var result = await GenerateThumbnailFile(videoPath, thumbnailPath, cancellationToken).ConfigureAwait(false);
            Log.LogInformation("Generate: success={Success}, thumbnailPath='{ThumbnailPath}'", result != null, thumbnailPath);
            return result;
        }
        catch (Exception e) {
            Log.LogError(e, "Generate failed for '{VideoPath}'", videoPath);
            return null;
        }
    }

    private static async Task<VideoThumbnail?> GenerateThumbnailFile(FilePath videoPath, FilePath thumbnailPath, CancellationToken cancellationToken)
    {
        var url = NSUrl.CreateFileUrl(videoPath);
        var asset = new AVUrlAsset(url);

        // Load video tracks to ensure the asset metadata is loaded
        var tracks = await asset.LoadTracksWithMediaTypeAsync(AVMediaTypes.Video.GetConstant()!)
            .ConfigureAwait(false);
        if (tracks.Count == 0)
            return null;

        var generator = new AVAssetImageGenerator(asset) {
            AppliesPreferredTrackTransform = true,
            MaximumSize = new CGSize(512, 512),
        };

        // Extract frame at 10% of duration (similar to server-side thumbnail)
        var frameTime = asset.Duration.Seconds >= 0.5
            ? TimeSpan.FromSeconds(0.5)
            : TimeSpan.FromSeconds(asset.Duration.Seconds * 0.1);
        var cgImage = await generator.GenerateCGImage(frameTime, cancellationToken).ConfigureAwait(false);
        if (cgImage == null)
            return null;

        using var uiImage = new UIImage(cgImage);
        using var jpegData = uiImage.AsJPEG(0.85f);

        if (jpegData == null)
            return null;

        await File.WriteAllBytesAsync(thumbnailPath, jpegData.ToArray(), cancellationToken).ConfigureAwait(false);
        return new VideoThumbnail(thumbnailPath, new Size((int)uiImage.Size.Width, (int)uiImage.Size.Height));
    }

    private static Task<Size> GetImageSize(FilePath imagePath)
    {
        using var uiImage = UIImage.FromFile(imagePath);
        return Task.FromResult(uiImage != null
            ? new Size((int)uiImage.Size.Width, (int)uiImage.Size.Height)
            : default);
    }
}
