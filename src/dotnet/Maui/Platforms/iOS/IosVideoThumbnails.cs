using ActualLab.IO;
using AVFoundation;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui;

public class IosVideoThumbnails(IServiceProvider services)
{
    private static readonly FilePath CacheDir = new FilePath(FileSystem.CacheDirectory) | "video-thumbnails";

    private ILogger Log => field ??= services.LogFor<IosVideoThumbnails>();

    // TODO: cancellation
    public async Task<FilePath> Generate(FilePath videoPath)
    {
        try {
            // Create a stable thumbnail path based on the source file name
            var thumbnailFileName = videoPath.FileName.ChangeExtension(".jpg");
            Directory.CreateDirectory(CacheDir);
            var thumbnailPath = CacheDir | thumbnailFileName;

            // Return cached thumbnail if it exists
            if (File.Exists(thumbnailPath)) {
                Log.LogInformation("Generate: using cached thumbnail '{ThumbnailPath}'", thumbnailPath);
                return thumbnailPath;
            }

            var success = await GenerateThumbnailFile(videoPath, thumbnailPath).ConfigureAwait(false);
            Log.LogInformation("Generate: success={Success}, thumbnailPath='{ThumbnailPath}'", success, thumbnailPath);
            return success ? thumbnailPath : FilePath.Empty;
        }
        catch (Exception e) {
            Log.LogError(e, "Generate failed for '{VideoPath}'", videoPath);
            return FilePath.Empty;
        }
    }

    private static async Task<bool> GenerateThumbnailFile(FilePath videoPath, FilePath thumbnailPath)
    {
        var url = NSUrl.CreateFileUrl(videoPath);
        var asset = new AVUrlAsset(url);

        // Load video tracks to ensure the asset metadata is loaded
        var tracks = await asset.LoadTracksWithMediaTypeAsync(AVMediaTypes.Video.GetConstant()!)
            .ConfigureAwait(false);
        if (tracks.Count == 0)
            return false;

        var generator = new AVAssetImageGenerator(asset) {
            AppliesPreferredTrackTransform = true,
            MaximumSize = new CGSize(512, 512),
        };

        // Extract frame at 10% of duration (similar to server-side thumbnail)
        var frameTime = asset.Duration.Seconds >= 0.5
            ? TimeSpan.FromSeconds(0.5)
            : TimeSpan.FromSeconds(asset.Duration.Seconds * 0.1);
        var cgImage = await generator.GenerateCGImage(frameTime).ConfigureAwait(false);
        if (cgImage == null)
            return false;

        using var uiImage = new UIImage(cgImage);
        using var jpegData = uiImage.AsJPEG(0.85f);

        if (jpegData == null)
            return false;

        await File.WriteAllBytesAsync(thumbnailPath, jpegData.ToArray()).ConfigureAwait(false);
        return true;
    }
}
