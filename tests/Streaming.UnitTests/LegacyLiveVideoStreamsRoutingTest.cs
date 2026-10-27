using ActualLab.Rpc;

namespace ActualChat.Streaming.UnitTests;

public sealed class LegacyLiveVideoStreamsRoutingTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Version LastOldPeer = new(2, 22, 9999);
    private static readonly Version FirstNewPeer = new(2, 23);

    [Theory]
#pragma warning disable CS0618 // The legacy methods are the subject here
    [InlineData(nameof(ILiveVideoStreams.LegacyMaxRequestedLayerId), "MaxRequestedLayerId")]
    [InlineData(nameof(ILiveVideoStreams.LegacyRequestedLayersMask), "RequestedLayersMask")]
    [InlineData(nameof(ILiveVideoStreams.LegacyThumbnailViewersOnly), "ThumbnailViewersOnly")]
#pragma warning restore CS0618
    public void AnOldPeerShouldReachTheLegacyMethod(string legacyMethodName, string wireName)
    {
        // arrange
        var legacyMethod = typeof(ILiveVideoStreams).GetMethod(legacyMethodName)!;

        // act
        var oldPeer = LegacyNameOf(legacyMethod, LastOldPeer);
        var newPeer = LegacyNameOf(legacyMethod, FirstNewPeer);

        // assert
        oldPeer?.Name.Should().Be(wireName, "2.12-2.22 clients still fall back to it when DemandInfo fails");
        newPeer.Should().BeNull("2.23 clients have no fallback, so the old wire name is not theirs");
    }

    // Private methods

    private static LegacyName? LegacyNameOf(MethodInfo method, Version version)
        => new LegacyNames(method)[version];
}
