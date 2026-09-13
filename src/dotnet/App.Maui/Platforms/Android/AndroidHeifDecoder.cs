using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Generators;
using ActualLab.IO;
using Android.Graphics;
using Uri = Android.Net.Uri;

namespace ActualChat.App.Maui;

/// <summary>
/// Android WebView can't decode HEIF, so HEIC/HEIF photos are decoded natively, downscaled to the
/// preset size and handed to the JS image processor as a q100 JPEG.
/// </summary>
public static class AndroidHeifDecoder
{
    private static readonly FilePath DecodedDirectory = MauiProcessedImageStore.RootDirectory | "decoded";

    public static Task<string?> TryDecodeToJpeg(
        string uri,
        ImageQualityBudget budget,
        CancellationToken cancellationToken)
        // Returns null when the URI isn't HEIF; the check is a binder call, so it runs off the dispatcher too
        => Task.Run<string?>(() => {
            if (!IsHeif(uri))
                return null;

            var source = ImageDecoder.CreateSource(Platform.AppContext.ContentResolver!, Uri.Parse(uri)!);
            using var bitmap = ImageDecoder.DecodeBitmap(source, new TargetSizeListener(budget));
            cancellationToken.ThrowIfCancellationRequested();

            Directory.CreateDirectory(DecodedDirectory);
            var filePath = DecodedDirectory | $"{RandomStringGenerator.Default.Next(12)}.jpg";
            using (var output = File.Create(filePath))
                bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 100, output);
            return Uri.FromFile(new Java.IO.File(filePath.Value))!.ToString()!;
        }, cancellationToken);

    // Private methods

    private static bool IsHeif(string uri)
        // ContentResolver.GetType returns null for "file://" URIs, which is what the extensions cover
        => Platform.AppContext.ContentResolver!.GetType(Uri.Parse(uri)!) is "image/heic" or "image/heif"
            || uri.EndsWith(".heic", StringComparison.OrdinalIgnoreCase)
            || uri.EndsWith(".heif", StringComparison.OrdinalIgnoreCase);

    // Nested types

    private sealed class TargetSizeListener(ImageQualityBudget budget)
        : Java.Lang.Object, ImageDecoder.IOnHeaderDecodedListener
    {
        public void OnHeaderDecoded(ImageDecoder decoder, ImageDecoder.ImageInfo info, ImageDecoder.Source source)
        {
            // Bitmap.Compress needs pixels in software memory, not a hardware bitmap
            decoder.Allocator = ImageDecoderAllocator.Software;
            var size = info.Size!;
            var width = size.Width;
            var height = size.Height;
            // Same two-limit fit as fitWithinBudget (image-geometry.ts): the pixel budget and the
            // long-side cap both apply, whichever binds tighter for this aspect ratio
            var pixelScale = budget.MaxPixels is not { } maxPixels
                ? 1.0
                : Math.Sqrt(maxPixels / ((double)width * height));
            var longSide = Math.Max(width, height);
            var sideScale = budget.MaxLongSide is not { } maxLongSide ? 1.0 : (double)maxLongSide / longSide;
            var scale = Math.Min(1.0, Math.Min(pixelScale, sideScale));
            if (scale >= 1)
                return;

            decoder.SetTargetSize(
                Math.Max(1, (int)Math.Round(width * scale)),
                Math.Max(1, (int)Math.Round(height * scale)));
        }
    }
}
