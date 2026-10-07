namespace ActualChat.Core.UnitTests;

public class VideoCodecExtTest
{
    [Theory]
    [InlineData("avc1.42E01E", "h264")]
    [InlineData("avc3", "h264")]
    [InlineData("HVC1.1.6.L93.90", "hevc")]
    [InlineData("hev1", "hevc")]
    [InlineData("vp09.00.31.08", "vp9")]
    [InlineData("av01.0.05M.08", "av1")]
    [InlineData("hevc", "hevc")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("unknown", null)]
    public void CodecShouldMapToCategory(string? codec, string? expected)
    {
        // act
        var category = codec.GetCategory();

        // assert
        category.Should().Be(expected);
    }

    [Theory]
    [InlineData("hvc1", "hevc", true)]
    [InlineData("hev1", "hvc1", true)]
    [InlineData("vp09", "vp9", true)]
    [InlineData("hvc1", "vp9", false)]
    [InlineData("unknown", "unknown", false)]
    [InlineData(null, "vp9", false)]
    public void SupportShouldMatchCodecCategory(string? codec, string supported, bool expected)
    {
        // act
        var isSupported = codec.IsSupported([supported]);

        // assert
        isSupported.Should().Be(expected);
    }
}
