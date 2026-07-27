using System.Security;
using ActualChat.Chat;
using ActualChat.Media;
using ActualChat.Testing.Host;
using ActualChat.Video;
using ActualLab.Rpc;

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

    [Fact(Timeout = 60_000)]
    public async Task NonMemberCannotReadOrControlVideoStream()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var memberSession = Session.New();
        _ = await appHost.SignIn(memberSession, new AccountFull("Bobby"));
        var nonMemberSession = Session.New();
        _ = await appHost.SignIn(nonMemberSession, new AccountFull("Jack"));

        var chat = await commander.Call(new Chats_Change(memberSession, default, null, new() {
            Create = new ChatDiff {
                Title = "NonMemberVideoTest",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var backend = services.GetRequiredService<IVideoStreamingBackend>();
        var liveStreams = services.GetRequiredService<ILiveVideoStreams>();
        var streamId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var record = new VideoRecord(
            streamId,
            memberSession,
            chat.Id,
            SystemClock.Instance.Now.EpochOffset.TotalSeconds,
            new VideoFormat {
                Codec = "avc1",
                Size = new Size2D(320, 240),
                SourceSize = new Size2D(320, 240),
            });

        // act
        await backend.PushVideo(record, new RpcStream<VideoFrameBundle>(GetBundles()), cts.Token);
        var memberStream = await liveStreams.GetStream(memberSession, streamId, cts.Token);

        // assert
        memberStream.Should().NotBeNull();
        await Assert.ThrowsAsync<SecurityException>(
            () => liveStreams.GetStream(nonMemberSession, streamId, cts.Token));
        await Assert.ThrowsAsync<SecurityException>(
            () => liveStreams.RequestKeyFrame(nonMemberSession, streamId.Value, cts.Token));
        await Assert.ThrowsAsync<SecurityException>(
            () => liveStreams.GetDemandStats(nonMemberSession, streamId, cts.Token));

        var nonMemberDemand = await liveStreams.DemandInfo(nonMemberSession, streamId, cts.Token);
        nonMemberDemand.Should().Be(StreamDemandInfo.None);
    }

    [Fact(Timeout = 60_000)]
    public async Task PlaybackQualityIgnoresStreamsTheCallerCannotRead()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var memberSession = Session.New();
        _ = await appHost.SignIn(memberSession, new AccountFull("Bobby"));
        var nonMemberSession = Session.New();
        _ = await appHost.SignIn(nonMemberSession, new AccountFull("Jack"));

        var chat = await commander.Call(new Chats_Change(memberSession, default, null, new() {
            Create = new ChatDiff {
                Title = "PlaybackQualityTest",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var backend = services.GetRequiredService<IVideoStreamingBackend>();
        var liveStreams = services.GetRequiredService<ILiveVideoStreams>();
        var streamId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var record = new VideoRecord(
            streamId,
            memberSession,
            chat.Id,
            SystemClock.Instance.Now.EpochOffset.TotalSeconds,
            new VideoFormat {
                Codec = "avc1",
                Size = new Size2D(320, 240),
                SourceSize = new Size2D(320, 240),
            });
        await backend.PushVideo(record, new RpcStream<VideoFrameBundle>(GetBundles()), cts.Token);

        // act
        var quality = new ApiMap<string, ReceiveQuality> { [streamId.Value] = new(1) };
        await liveStreams.ChangePlaybackQuality(nonMemberSession, quality, null, cts.Token);

        // assert
        var stats = await backend.GetDemandStats(streamId, cts.Token);
        stats.ViewerCount.Should().Be(0);

        await liveStreams.ChangePlaybackQuality(memberSession, quality, null, cts.Token);
        stats = await backend.GetDemandStats(streamId, cts.Token);
        stats.ViewerCount.Should().Be(1);
    }

    [Fact(Timeout = 60_000)]
    public async Task PlaybackQualityIgnoresUnparsableStreamIds()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));
        var liveStreams = services.GetRequiredService<ILiveVideoStreams>();
        var quality = new ApiMap<string, ReceiveQuality> { ["not-a-stream-id"] = new(1) };

        // act
        var change = () => liveStreams.ChangePlaybackQuality(session, quality, null, CancellationToken.None);

        // assert
        await change.Should().NotThrowAsync();
    }

    // Private methods

    private static async IAsyncEnumerable<VideoFrameBundle> GetBundles()
    {
        var offset = TimeSpan.Zero;
        var duration = TimeSpan.FromMilliseconds(33);
        for (var i = 0; i < 5; i++) {
            var data = new byte[64];
            Array.Fill(data, (byte)i);
            yield return new VideoFrameBundle([
                new VideoFrame {
                    Data = data,
                    Offset = offset,
                    Duration = duration,
                    Index = i,
                    KeyFrameIndex = i,
                    Width = 320,
                    Height = 240,
                    Codec = "avc1",
                },
            ]);

            offset += duration;
            await Task.Delay(20);
        }
    }
}
