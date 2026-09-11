using System.Text;
using ActualChat.Uploads;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class ImageMetadataStripperTest
{
    private static readonly byte[] Soi = [0xFF, 0xD8];
    private static readonly byte[] Icc = JpegSegment(0xE2, Ascii("ICC_PROFILE\0"), [1, 1, 0xAA, 0xBB]);
    private static readonly byte[] Mpf = JpegSegment(0xE2, Ascii("MPF\0"), [0x4D, 0x4D, 0x00, 0x2A]);
    private static readonly byte[] Xmp = JpegSegment(0xE1, Ascii("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta>creator</x:xmpmeta>"));
    private static readonly byte[] XmpHdr = JpegSegment(0xE1, Ascii("http://ns.adobe.com/xap/1.0/\0<x hdrgm:Version=\"1.0\"/>"));
    private static readonly byte[] Comment = JpegSegment(0xFE, Ascii("secret comment"));
    private static readonly byte[] Iptc = JpegSegment(0xED, Ascii("Photoshop 3.0\0"));
    private static readonly byte[] Dqt = JpegSegment(0xDB, [0x00, .. Enumerable.Repeat((byte)1, 64)]);
    private static readonly byte[] Scan = [0xFF, 0xDA, 0x00, 0x08, 1, 1, 0, 0, 63, 0, 0x12, 0x34, 0xFF, 0xD9];
    private static readonly byte[] GainMap = [0xFF, 0xD8, 0x55, 0xFF, 0xD9];

    [Fact]
    public void JpegShouldLoseExifXmpIptcAndCommentsButKeepIccImageDataAndTrailer()
    {
        // arrange
        var input = Concat(Soi, JpegSegment(0xE1, ExifPayload(1)), Xmp, Iptc, Comment, Icc, Dqt, Scan, GainMap);

        // act
        var result = ImageMetadataStripper.Strip(input);

        // assert
        result.Should().Equal(Concat(Soi, Icc, Dqt, Scan, GainMap));
    }

    [Fact]
    public void JpegShouldKeepOnlyOrientationWhenItIsNotOne()
    {
        // arrange
        var input = Concat(Soi, JpegSegment(0xE1, ExifPayload(6)), Dqt, Scan);

        // act
        var result = ImageMetadataStripper.Strip(input);

        // assert
        Contains(result, "GPSSECRET").Should().BeFalse();
        result[..6].Should().Equal(0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x22);
        result[31].Should().Be(6);
        result[38..].Should().Equal(Concat(Dqt, Scan));
    }

    [Fact]
    public void JpegShouldReturnInputInstanceWhenOrientationExifIsAlreadyMinimal()
    {
        // arrange
        var input = Concat(Soi, JpegSegment(0xE1, ExifPayload(6)), Dqt, Scan);
        var stripped = ImageMetadataStripper.Strip(input);

        // act
        var result = ImageMetadataStripper.Strip(stripped);

        // assert
        result.Should().BeSameAs(stripped);
    }

    [Fact]
    public void JpegShouldKeepUltraHdrXmp()
    {
        // arrange
        var input = Concat(Soi, XmpHdr, Comment, Dqt, Scan);

        // act
        var result = ImageMetadataStripper.Strip(input);

        // assert
        result.Should().Equal(Concat(Soi, XmpHdr, Dqt, Scan));
    }

    [Fact]
    public void JpegShouldNotMoveSegmentsAfterMpf()
    {
        // arrange
        var input = Concat(Soi, Comment, Mpf, Comment, Dqt, Scan);

        // act
        var result = ImageMetadataStripper.Strip(input);

        // assert
        result.Should().Equal(Concat(Soi, Mpf, Comment, Dqt, Scan));
    }

    [Fact]
    public void ShouldReturnInputInstanceWhenNothingChangesOrFileIsMalformed()
    {
        // arrange
        var clean = Concat(Soi, Dqt, Scan);
        var truncated = Concat(Soi, [0xFF, 0xE1, 0x10, 0x00, 0x01]);
        var notAnImage = Ascii("hello");

        // act & assert
        ImageMetadataStripper.Strip(clean).Should().BeSameAs(clean);
        ImageMetadataStripper.Strip(truncated).Should().BeSameAs(truncated);
        ImageMetadataStripper.Strip(notAnImage).Should().BeSameAs(notAnImage);
    }

    [Fact]
    public void PngShouldLoseTextTimeAndExifChunks()
    {
        // arrange
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var ihdr = PngChunk("IHDR", new byte[13]);
        var iccp = PngChunk("iCCP", Concat(Ascii("icc\0"), [0, 9]));
        var idat = PngChunk("IDAT", [7, 7]);
        var iend = PngChunk("IEND", []);
        var input = Concat(
            signature, ihdr, PngChunk("tEXt", Ascii("Author\0me")), PngChunk("eXIf", [1, 2]),
            PngChunk("iTXt", [0]), PngChunk("zTXt", [0]), PngChunk("tIME", new byte[7]), iccp, idat, iend);

        // act
        var result = ImageMetadataStripper.Strip(input);

        // assert
        result.Should().Equal(Concat(signature, ihdr, iccp, idat, iend));
    }

    [Fact]
    public void WebpShouldLoseExifAndXmpChunksAndFixHeader()
    {
        // arrange
        var vp8x = WebpChunk("VP8X", [0x0C | 0x10, 0, 0, 0, 1, 0, 0, 1, 0, 0]);
        var iccp = WebpChunk("ICCP", [9, 9]);
        var vp8 = WebpChunk("VP8 ", [5, 5, 5]);
        var body = Concat(
            Ascii("WEBP"), vp8x, iccp, vp8, WebpChunk("EXIF", Ascii("GPSSECRET")), WebpChunk("XMP ", [1, 2]));
        var input = Concat(Ascii("RIFF"), BitConverter.GetBytes((uint)body.Length), body);

        // act
        var result = ImageMetadataStripper.Strip(input);

        // assert
        var expectedBody = Concat(Ascii("WEBP"), vp8x, iccp, vp8);
        expectedBody[12] = 0x10;
        result.Should().Equal(Concat(Ascii("RIFF"), BitConverter.GetBytes((uint)expectedBody.Length), expectedBody));
    }

    // Private methods

    private static byte[] ExifPayload(byte orientation)
        => Concat(
            Ascii("Exif\0\0"),
            [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x02, 0x00],
            [0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, orientation, 0x00, 0x00, 0x00],
            [0x25, 0x88, 0x04, 0x00, 0x01, 0x00, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00],
            [0x00, 0x00, 0x00, 0x00],
            Ascii("GPSSECRET"));

    private static byte[] JpegSegment(byte marker, params byte[][] payloadParts)
    {
        var payload = Concat(payloadParts);
        var length = payload.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)length, .. payload];
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        var length = BitConverter.GetBytes((uint)data.Length);
        Array.Reverse(length);
        return Concat(length, Ascii(type), data, [1, 2, 3, 4]);
    }

    private static byte[] WebpChunk(string fourCC, byte[] data)
        => Concat(
            Ascii(fourCC), BitConverter.GetBytes((uint)data.Length), data,
            data.Length % 2 == 1 ? new byte[] { 0 } : []);

    private static byte[] Ascii(string text)
        => Encoding.ASCII.GetBytes(text);

    private static byte[] Concat(params byte[][] parts)
        => parts.SelectMany(x => x).ToArray();

    private static bool Contains(byte[] data, string text)
        => data.AsSpan().IndexOf(Ascii(text)) >= 0;
}
