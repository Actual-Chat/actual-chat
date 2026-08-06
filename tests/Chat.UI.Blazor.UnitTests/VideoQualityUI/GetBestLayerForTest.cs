using static ActualChat.UI.Blazor.App.Services.VideoQualityUI;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class GetBestLayerForTest
{
    [Theory]
    [InlineData(VideoSize.None, 1)]      // no hint → top layer
    [InlineData(VideoSize.W320, 0)]      // smaller than ladder → L0
    [InlineData(VideoSize.W640, 0)]
    [InlineData(VideoSize.W960, 0)]      // exact L0 match
    [InlineData(VideoSize.W1280, 1)]     // between L0 and L1 → smallest ≥ desired = L1 (regression)
    [InlineData(VideoSize.W1920, 1)]     // exact L1 match
    [InlineData(VideoSize.W3840, 1)]     // bigger than top → fall back to top
    public void Screencast_PicksSmallestLayerAtLeastAsLargeAsDesired(VideoSize desired, int expectedLayer)
        => GetBestLayerFor(VideoSourceKind.ScreenCast, desired).Should().Be(expectedLayer);

    [Theory]
    [InlineData(VideoSize.None, 2)]
    [InlineData(VideoSize.W320, 0)]
    [InlineData(VideoSize.W480, 1)]
    [InlineData(VideoSize.W640, 1)]
    [InlineData(VideoSize.W960, 2)]
    [InlineData(VideoSize.W1280, 2)]
    [InlineData(VideoSize.W1920, 2)]
    public void Camera_PicksSmallestLayerAtLeastAsLargeAsDesired(VideoSize desired, int expectedLayer)
        => GetBestLayerFor(VideoSourceKind.Camera, desired).Should().Be(expectedLayer);
}
