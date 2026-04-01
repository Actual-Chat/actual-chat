using ActualChat.Chat;
using ActualChat.Testing.Host;
using ActualChat.Video;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class VideoQualityPresetTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task GetQualityPreset_ShouldReturnHighByDefault()
    {
        var backend = AppHost.Services.GetRequiredService<IVideoStreamingBackend>();
        var unknownStreamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);

        var preset = await backend.GetQualityPreset(unknownStreamId, CancellationToken.None);

        preset.Should().NotBeNull();
        preset.Level.Should().Be(VideoQualityLevel.High,
            "unknown stream should default to High quality");
    }

    [Fact]
    public async Task GetQualityPreset_ShouldBeComputedMethod()
    {
        var backend = AppHost.Services.GetRequiredService<IVideoStreamingBackend>();
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);

        // Should work with Computed.Capture
        var computed = await Computed.Capture(
            () => backend.GetQualityPreset(streamId, CancellationToken.None));

        computed.Value.Level.Should().Be(VideoQualityLevel.High);
        computed.IsConsistent().Should().BeTrue();
    }
}
