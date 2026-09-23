using System.Buffers.Binary;
using ActualChat.Uploads;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
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

    public static byte[] CreateJpegWithExif(int width, int height, ushort orientation)
    {
        using var image = new Image<Rgba32>(width, height);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Orientation, orientation);
        exif.SetValue(ExifTag.Software, "GPSSECRET");
        image.Metadata.ExifProfile = exif;
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

    public static byte[] CreateAnimatedGif(int width, int height, int frameCount)
    {
        using var image = new Image<Rgba32>(width, height);
        for (var i = 1; i < frameCount; i++)
            image.Frames.AddFrame(image.Frames.RootFrame);
        using var ms = new MemoryStream();
        image.Save(ms, new GifEncoder());
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

    // A structurally valid HEIC with no decodable picture: the primary item's bytes are a fixed marker,
    // so a test can tell whether they moved. An item stored in idat uses iloc construction method 1.
    public static byte[] CreateHeif(
        int width,
        int height,
        int rotation = 0,
        byte[]? exif = null,
        byte[]? xmp = null,
        bool isXmpInIdat = false)
    {
        var items = new List<(ushort Id, string Type, byte[] Data, bool IsInIdat)> {
            (1, "hvc1", HeifImageData.ToArray(), false),
        };
        if (exif is not null)
            items.Add((2, "Exif", exif, false));
        if (xmp is not null)
            items.Add((3, "mime", xmp, isXmpInIdat));

        var ftyp = HeifBox("ftyp", [.."heic"u8, 0, 0, 0, 0, .."mif1"u8, .."heic"u8]);
        var idat = items.Where(i => i.IsInIdat).SelectMany(i => i.Data).ToArray();
        // iloc offsets depend on the meta box size, which doesn't depend on the offsets' values
        var meta = CreateHeifMeta(items, rotation, width, height, idat, 0);
        var mdatStart = ftyp.Length + meta.Length + 8;
        meta = CreateHeifMeta(items, rotation, width, height, idat, mdatStart);
        var mdat = HeifBox("mdat", items.Where(i => !i.IsInIdat).SelectMany(i => i.Data).ToArray());
        return [..ftyp, ..meta, ..mdat];
    }

    public static ReadOnlySpan<byte> HeifImageData => "HEVC-IMAGE-DATA-MARKER"u8;

    // Private methods

    private static byte[] CreateHeifMeta(
        List<(ushort Id, string Type, byte[] Data, bool IsInIdat)> items,
        int rotation,
        int width,
        int height,
        byte[] idat,
        int mdatStart)
    {
        var hdlr = HeifBox("hdlr", [0, 0, 0, 0, 0, 0, 0, 0, .."pict"u8, .. new byte[12], 0]);
        var pitm = HeifBox("pitm", [0, 0, 0, 0, 0, 1]);
        var infos = items.SelectMany(i => HeifBox("infe", [
            2, 0, 0, 0, 0, (byte)i.Id, 0, 0, ..System.Text.Encoding.ASCII.GetBytes(i.Type), 0,
            ..(i.Type == "mime" ? [.."application/rdf+xml"u8, 0] : Array.Empty<byte>()),
        ])).ToArray();
        var iinf = HeifBox("iinf", [0, 0, 0, 0, 0, (byte)items.Count, ..infos]);
        var ispe = HeifBox("ispe", [0, 0, 0, 0, ..BigEndian(width), ..BigEndian(height)]);
        var irot = HeifBox("irot", [(byte)(rotation / 90 & 3)]);
        var ipco = HeifBox("ipco", [..ispe, ..irot]);
        var ipma = HeifBox("ipma", [0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 2, 0x81, 0x02]);
        var iprp = HeifBox("iprp", [..ipco, ..ipma]);
        var locations = new List<byte>();
        int mdatOffset = mdatStart, idatOffset = 0;
        foreach (var item in items) {
            var offset = item.IsInIdat ? idatOffset : mdatOffset;
            locations.AddRange([0, (byte)item.Id, 0, (byte)(item.IsInIdat ? 1 : 0), 0, 0, 0, 1]);
            locations.AddRange([..BigEndian(offset), ..BigEndian(item.Data.Length)]);
            if (item.IsInIdat)
                idatOffset += item.Data.Length;
            else
                mdatOffset += item.Data.Length;
        }
        var iloc = HeifBox("iloc", [1, 0, 0, 0, 0x44, 0x00, 0, (byte)items.Count, ..locations]);
        var idatBox = idat.Length > 0 ? HeifBox("idat", idat) : [];
        return HeifBox("meta", [0, 0, 0, 0, ..hdlr, ..pitm, ..iinf, ..iprp, ..idatBox, ..iloc]);
    }

    private static byte[] HeifBox(string type, byte[] body)
        => [..BigEndian(8 + body.Length), ..System.Text.Encoding.ASCII.GetBytes(type), ..body];

    private static byte[] BigEndian(int value)
    {
        var result = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(result, value);
        return result;
    }

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
