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

    public static UploadedStreamFile CreateUploadedFile(string fileName, string contentType, byte[] data)
        => new(fileName, contentType, data.Length, () => Task.FromResult<Stream>(new MemoryStream(data)));
}
