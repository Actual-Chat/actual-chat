using ActualChat.Uploads;

namespace ActualChat.Core.Server.UnitTests.Uploads;

public class HeifReaderTest
{
    [Theory]
    [InlineData(0, 4032, 3024)]
    [InlineData(90, 3024, 4032)]
    [InlineData(180, 4032, 3024)]
    [InlineData(270, 3024, 4032)]
    public void DisplaySizeShouldApplyIrot(int rotation, int expectedWidth, int expectedHeight)
    {
        // arrange - phones store the sensor-sized picture and rotate it with irot
        var data = TestImages.CreateHeif(4032, 3024, rotation);

        // act
        var size = HeifReader.ReadDisplaySize(data);

        // assert
        size.Should().Be(new Size2D(expectedWidth, expectedHeight));
    }

    [Fact]
    public void DisplaySizeShouldBeNullForNonHeifOrTruncatedData()
    {
        // arrange
        var heif = TestImages.CreateHeif(640, 480, exif: Exif("SECRET"));

        // act & assert
        HeifReader.ReadDisplaySize(TestImages.CreateJpeg(10, 10)).Should().BeNull();
        HeifReader.ReadDisplaySize(heif.AsSpan(0, heif.Length / 3)).Should().BeNull();
        HeifReader.ReadDisplaySize([]).Should().BeNull();
    }

    [Fact]
    public void MetadataRangesShouldCoverExifAndXmpInMdatAndIdat()
    {
        // arrange
        var exif = Exif("SECRET");
        var xmp = "<x:xmpmeta>creator</x:xmpmeta>"u8.ToArray();
        var data = TestImages.CreateHeif(640, 480, exif: exif, xmp: xmp, isXmpInIdat: true);

        // act
        var ranges = HeifReader.GetMetadataRanges(data, mustKeepHdrXmp: true);

        // assert
        ranges.Should().NotBeNull();
        ranges!.Select(r => data[r.Range]).Should().BeEquivalentTo([exif, xmp]);
        ranges.Select(r => r.IsExif).Should().Equal(true, false);
    }

    [Fact]
    public void MetadataRangesShouldSkipHdrGainMapXmpWhenAsked()
    {
        // arrange
        var data = TestImages.CreateHeif(640, 480, xmp: "<x hdrgm:Version=\"1.0\"/>"u8.ToArray());

        // act & assert
        HeifReader.GetMetadataRanges(data, mustKeepHdrXmp: true).Should().BeEmpty();
        HeifReader.GetMetadataRanges(data, mustKeepHdrXmp: false).Should().HaveCount(1);
    }

    internal static byte[] Exif(string secret)
        // Offset 6 to the TIFF header past "Exif\0\0", then a TIFF header and a payload to find later
        => [
            0, 0, 0, 6, .."Exif\0\0"u8,
            0x4D, 0x4D, 0x00, 0x2A, 0, 0, 0, 0x08,
            .. System.Text.Encoding.ASCII.GetBytes(secret),
        ];
}
