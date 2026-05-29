using ActualChat.Media;
using ActualChat.Streaming;
using static ActualChat.UI.Blazor.App.Services.VideoQualityUI;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class PlaybackVerdictClassifierTest
{
    private static readonly PlaybackThresholds T = PlaybackThresholds.Defaults;

    [Fact]
    public void TargetBuffer_ReturnsGood()
        => PlaybackVerdictClassifier.Classify(Constants.Video.BufferDurationTooLowMs, T).Should().Be(1);

    [Fact]
    public void LowButPositiveBuffer_StillReturnsGood()
        => PlaybackVerdictClassifier
            .Classify(Constants.Video.BufferDurationTooLowMs - 1, T)
            .Should().Be(1);

    [Fact]
    public void ZeroBuffer_ReturnsNeutral()
        => PlaybackVerdictClassifier.Classify(0, T).Should().Be(0);

    [Fact]
    public void TooMuchBuffer_ReturnsNeutral()
        => PlaybackVerdictClassifier
            .Classify(T.BufferDurationTooHighMs + 1, T)
            .Should().Be(0);

    [Fact]
    public void TooHighBoundary_ReturnsGood()
        => PlaybackVerdictClassifier.Classify(T.BufferDurationTooHighMs, T).Should().Be(1);
}

public class VideoSizeTest
{
    [Theory]
    [InlineData(VideoSize.W1920, 1080)]
    [InlineData(VideoSize.W1280, 720)]
    [InlineData(VideoSize.W960, 540)]
    [InlineData(VideoSize.W640, 360)]
    [InlineData(VideoSize.W320, 180)]
    public void Height16X9_ReturnsExpectedHeight(VideoSize size, int expectedHeight)
        => size.ShortSide().Should().Be(expectedHeight);

    [Theory]
    [InlineData(0, 0, VideoSize.None)]
    [InlineData(500, 1, VideoSize.W640)]
    [InlineData(500, 2, VideoSize.W1280)]
    [InlineData(480, 3, VideoSize.W1280)]
    [InlineData(960, 2, VideoSize.W1920)]
    public void PickForRenderSize_UsesCssAndCappedDpr(
        double cssLongSide,
        double devicePixelRatio,
        VideoSize expected)
        => VideoSizeExt.FromLongSide(cssLongSide, devicePixelRatio).Should().Be(expected);

    [Fact]
    public void VideoLayerDefs_ExposeH264BaseBitratesKbpsAndCodecEfficiencies()
    {
        VideoLayerDef.CameraLayers.Should().Equal(
            new VideoLayerDef(VideoSourceKind.Camera, VideoSize.W320, 312.5),
            new VideoLayerDef(VideoSourceKind.Camera, VideoSize.W640, 1_250),
            new VideoLayerDef(VideoSourceKind.Camera, VideoSize.W1280, 4_000));
        VideoLayerDef.ScreenCastLayers.Should().Equal(
            new VideoLayerDef(VideoSourceKind.ScreenCast, VideoSize.W960, 4_375),
            new VideoLayerDef(VideoSourceKind.ScreenCast, VideoSize.W1920, 11_375));

        var videoConstants = new AppConstants.VideoConstants();
        videoConstants.CameraLayerBaseBitratesKbps.Should().Equal(312.5, 1_250d, 4_000d);
        videoConstants.ScreenCastLayerBaseBitratesKbps.Should().Equal(4_375d, 11_375d);
        videoConstants.CodecDefs.Should().Contain(new VideoCodecDef() { Kind = VideoCodecKind.H264, Efficiency = 1 });
        videoConstants.CodecDefs.Should().Contain(new VideoCodecDef() { Kind = VideoCodecKind.Hevc, Efficiency = 1.4 });
    }

    [Fact]
    public void VideoLayerDef_GetBitrateKbps_UsesCodecEfficiency()
    {
        var topCameraLayer = VideoLayerDef.CameraLayers[^1];

        topCameraLayer.GetBitrateKbps(VideoCodecKind.H264).Should().Be(4_000);
        topCameraLayer.GetBitrateKbps(VideoCodecKind.Hevc).Should().BeApproximately(4_000 / 1.4, 0.01);
        topCameraLayer.GetByteRate(VideoCodecKind.Hevc).Should().Be(357_143);
    }

    [Theory]
    [InlineData("avc1.640028", VideoCodecKind.H264)]
    [InlineData("h264", VideoCodecKind.H264)]
    [InlineData("hev1.1.6.L120.B0", VideoCodecKind.Hevc)]
    [InlineData("hvc1.1.6.L120.B0", VideoCodecKind.Hevc)]
    [InlineData("hevc", VideoCodecKind.Hevc)]
    [InlineData("vp09.00.41.08", VideoCodecKind.Vp9)]
    [InlineData("vp9", VideoCodecKind.Vp9)]
    [InlineData("av01.0.08M.08", VideoCodecKind.Av1)]
    [InlineData("bogus", VideoCodecKind.Unknown)]
    public void VideoCodecKind_Parse_ReturnsExpectedKind(string codec, VideoCodecKind expected)
        => VideoCodecKindExt.Parse(codec).Should().Be(expected);
}
