using ActualChat.App.Maui.Services;
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

    public static Task<string?> TryDecodeToJpeg(string uri, int maxSize, CancellationToken cancellationToken)
        // Returns null when the URI isn't HEIF; the check is a binder call, so it runs off the dispatcher too
        => Task.Run<string?>(() => {
            if (!IsHeif(uri))
                return null;

            var source = ImageDecoder.CreateSource(Platform.AppContext.ContentResolver!, Uri.Parse(uri)!);
            using var bitmap = ImageDecoder.DecodeBitmap(source, new TargetSizeListener(maxSize));
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

    private sealed class TargetSizeListener(int maxSize) : Java.Lang.Object, ImageDecoder.IOnHeaderDecodedListener
    {
        public void OnHeaderDecoded(ImageDecoder decoder, ImageDecoder.ImageInfo info, ImageDecoder.Source source)
        {
            // Bitmap.Compress needs pixels in software memory, not a hardware bitmap
            decoder.Allocator = ImageDecoderAllocator.Software;
            var size = info.Size!;
            var longSide = Math.Max(size.Width, size.Height);
            if (longSide <= maxSize)
                return;

            var scale = (double)maxSize / longSide;
            decoder.SetTargetSize(
                Math.Max(1, (int)Math.Round(size.Width * scale)),
                Math.Max(1, (int)Math.Round(size.Height * scale)));
        }
    }
}
