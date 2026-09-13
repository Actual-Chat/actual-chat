using ActualChat.UI.Blazor.Services;
using SixLabors.ImageSharp;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ImagePlaceholderTest
{
    // Packed bytes for a 128x96 flat-fill bitmap, captured from Task 6's
    // "should encode a landscape image with a negative short-side byte" test fixture.
    private static readonly byte[] StrippedFlatFillFixture = [
        1, 208, 255, 196, 0, 40, 0, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        4, 3, 16, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 218, 0, 12,
        3, 1, 0, 2, 0, 3, 0, 0, 63, 0, 156, 5, 205, 128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 1,
    ];

    [Fact]
    public void ShouldReturnEmptyForMissingInput()
    {
        // act & assert
        ImagePlaceholder.ToDataUrl(null).Should().BeEmpty();
        ImagePlaceholder.ToDataUrl("").Should().BeEmpty();
    }

    [Fact]
    public void ShouldReturnEmptyForAnUnknownFormatMark()
    {
        // arrange
        var packed = Convert.ToBase64String(new byte[] { 99, 32, 1, 2, 3 });

        // act & assert
        ImagePlaceholder.ToDataUrl(packed).Should().BeEmpty();
    }

    [Fact]
    public void ShouldRebuildAStrippedPlaceholderIntoAJpegDataUrl()
    {
        // arrange
        var packed = Convert.ToBase64String(new byte[] { 1, unchecked((byte)-48), 0xFF, 0xC4, 0x00, 0x02 });

        // act
        var url = ImagePlaceholder.ToDataUrl(packed);

        // assert
        url.Should().StartWith("data:image/jpeg;base64,");
        var bytes = Convert.FromBase64String(url["data:image/jpeg;base64,".Length..]);
        bytes[0].Should().Be(0xFF);
        bytes[1].Should().Be(0xD8); // SOI, i.e. the prefix really was prepended
    }

    [Fact]
    public void ShouldPassAFullJpegThrough()
    {
        // arrange
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var packed = Convert.ToBase64String(new byte[] { 2, 64 }.Concat(jpeg).ToArray());

        // act
        var url = ImagePlaceholder.ToDataUrl(packed);

        // assert
        Convert.FromBase64String(url["data:image/jpeg;base64,".Length..]).Should().Equal(jpeg);
    }

    [Fact]
    public void ShouldReturnEmptyForAZeroShortSide()
    {
        // arrange
        var packed = Convert.ToBase64String(new byte[] { 1, 0, 0xFF, 0xC4, 0x00, 0x02 });

        // act & assert
        ImagePlaceholder.ToDataUrl(packed).Should().BeEmpty();
    }

    [Fact]
    public void ShouldRebuildAStrippedPlaceholderThatIdentifiesAs64x48()
    {
        // arrange
        var packed = Convert.ToBase64String(StrippedFlatFillFixture);

        // act
        var url = ImagePlaceholder.ToDataUrl(packed);
        var bytes = Convert.FromBase64String(url["data:image/jpeg;base64,".Length..]);
        using var stream = new MemoryStream(bytes);
        var info = Image.Identify(stream);

        // assert
        info.Should().NotBeNull();
        info!.Width.Should().Be(64);
        info.Height.Should().Be(48);
    }
}
