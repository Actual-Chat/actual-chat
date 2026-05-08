using ActualChat.Chat;
using ActualChat.Testing.Host;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class KeyFrameRequestTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task GetKeyframeRequestAt_ShouldReturnSystemNow()
    {
        var backend = AppHost.Services.GetRequiredService<IVideoStreamingBackend>();
        var unknownStreamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);
        var before = MomentClockSet.Default.SystemClock.Now;

        var requestAt = await backend.LastKeyframeRequestAt(unknownStreamId, CancellationToken.None);

        requestAt.Should().BeGreaterThanOrEqualTo(before);
        requestAt.Should().BeLessThanOrEqualTo(MomentClockSet.Default.SystemClock.Now);
    }

    [Fact]
    public async Task GetKeyframeRequestAt_ShouldBeComputedMethod()
    {
        var backend = AppHost.Services.GetRequiredService<IVideoStreamingBackend>();
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);

        // Should work with Computed.Capture
        var computed = await Computed.Capture(
            () => backend.LastKeyframeRequestAt(streamId, CancellationToken.None));

        computed.Value.Should().NotBe(default);
        computed.IsConsistent().Should().BeTrue();
    }

    [Fact]
    public async Task RequestKeyFrame_ShouldInvalidateStaleRequestAt()
    {
        var backend = AppHost.Services.GetRequiredService<IVideoStreamingBackend>();
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);

        var computed = await Computed.Capture(
            () => backend.LastKeyframeRequestAt(streamId, CancellationToken.None));
        await Task.Delay(Constants.Video.KeyFrameRequestCooldown + TimeSpan.FromMilliseconds(100));

        await backend.RequestKeyFrame(streamId, CancellationToken.None);

        computed.IsConsistent().Should().BeFalse();
    }
}
