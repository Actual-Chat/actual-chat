using System.Text.RegularExpressions;
using ActualChat.UI.Blazor.Services;
using ActualLab.IO;
using SixLabors.ImageSharp;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed partial class ImagePlaceholderTest
{
    private static readonly FilePath TypeScriptEncoderPath
        = (FilePath)"src" & "nodejs" & "src" & "image-processing" & "placeholder-encoder.ts";

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
        ImagePlaceholder.ToDataUrl([]).Should().BeEmpty();
    }

    [Fact]
    public void ShouldReturnEmptyForAnUnknownFormatMark()
    {
        // arrange
        var packed = new byte[] { 99, 32, 1, 2, 3 };

        // act & assert
        ImagePlaceholder.ToDataUrl(packed).Should().BeEmpty();
    }

    [Fact]
    public void ShouldRebuildAStrippedPlaceholderIntoAJpegDataUrl()
    {
        // arrange
        var packed = new byte[] { 1, unchecked((byte)-48), 0xFF, 0xC4, 0x00, 0x02 };

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
        var packed = new byte[] { 2, 64 }.Concat(jpeg).ToArray();

        // act
        var url = ImagePlaceholder.ToDataUrl(packed);

        // assert
        Convert.FromBase64String(url["data:image/jpeg;base64,".Length..]).Should().Equal(jpeg);
    }

    [Fact]
    public void ShouldReturnEmptyForAZeroShortSide()
    {
        // arrange
        var packed = new byte[] { 1, 0, 0xFF, 0xC4, 0x00, 0x02 };

        // act & assert
        ImagePlaceholder.ToDataUrl(packed).Should().BeEmpty();
    }

    [Fact]
    public void PrefixShouldMatchTheTypeScriptOne()
    {
        // The two runtimes hand-copy these bytes (Ruling P2), and a one-byte DQT difference still
        // decodes as a 64x48 JPEG - just with the wrong colours, which no other test would catch

        // arrange
        var source = File.ReadAllText(FindRepoRoot() & TypeScriptEncoderPath);

        // act
        var literal = TypeScriptPrefixRe().Match(source);
        var bytes = literal.Success
            ? literal.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(byte.Parse)
                .ToArray()
            : null;

        // assert
        literal.Success.Should().BeTrue("PLACEHOLDER_PREFIX must still be a Uint8Array literal");
        bytes.Should().Equal(ImagePlaceholder.Prefix);
    }

    [Fact]
    public void ShouldRebuildAStrippedPlaceholderThatIdentifiesAs64x48()
    {
        // arrange
        var packed = StrippedFlatFillFixture;

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

    // Private methods

    private static FilePath FindRepoRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        for (var i = 0; i < 8 && directory is not null; i++) {
            var path = (FilePath)directory.FullName;
            if (File.Exists(path & "ActualChat.sln"))
                return path;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found (ActualChat.sln).");
    }

    [GeneratedRegex(@"PLACEHOLDER_PREFIX\s*=\s*new Uint8Array\(\[([\s\d,]*)\]\)")]
    private static partial Regex TypeScriptPrefixRe();
}
