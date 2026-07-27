using System.Buffers.Binary;
using ActualChat.Uploads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ActualChat.Testing;

public static class TestImages
{
    public const string DefaultJpg = "default.jpg";
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static Stream GetImage(string name)
    {
        var type = typeof(TestImages);
        return type.Assembly.GetManifestResourceStream($"{type.Namespace}.TestImages.{name}").Require();
    }

    public static UploadedStreamFile GetUploadedImage(string name)
    {
        var stream = GetImage(name);
        return new UploadedStreamFile(name, "image/jpeg", stream.Length, () => Task.FromResult(stream));
    }

    public static byte[] CreatePng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    public static byte[] CreateJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new JpegEncoder());
        return ms.ToArray();
    }

    public static byte[] CreateWebp(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new WebpEncoder());
        return ms.ToArray();
    }

    public static byte[] CreateBmp(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var ms = new MemoryStream();
        image.Save(ms, new BmpEncoder());
        return ms.ToArray();
    }

    public static byte[] CreateAnimatedWebp(int width, int height, int frameCount)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var i = 1; i < frameCount; i++)
            image.Frames.AddFrame(image.Frames.RootFrame);
        using var ms = new MemoryStream();
        image.Save(ms, new WebpEncoder());
        return ms.ToArray();
    }

    public static byte[] CreatePngHeader(int width, int height)
    {
        // Signature + IHDR + IEND: declares the given size without carrying any pixel data
        var result = new byte[45];
        var span = result.AsSpan();
        PngSignature.CopyTo(span);
        BinaryPrimitives.WriteInt32BigEndian(span[8..], 13);
        "IHDR"u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32BigEndian(span[16..], width);
        BinaryPrimitives.WriteInt32BigEndian(span[20..], height);
        span[24] = 8; // Bit depth
        span[25] = 6; // Color type: RGBA
        BinaryPrimitives.WriteUInt32BigEndian(span[29..], Crc32(span[12..29]));
        "IEND"u8.CopyTo(span[37..]);
        BinaryPrimitives.WriteUInt32BigEndian(span[41..], Crc32(span[37..41]));
        return result;
    }

    public static UploadedStreamFile CreateUploadedFile(string fileName, string contentType, byte[] data)
        => new(fileName, contentType, data.Length, () => Task.FromResult<Stream>(new MemoryStream(data)));

    // Private methods

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFF_FFFFu;
        foreach (var b in data) {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc >> 1) ^ (0xEDB8_8320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }
}
