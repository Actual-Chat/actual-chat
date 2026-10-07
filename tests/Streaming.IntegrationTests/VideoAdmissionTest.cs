using ActualChat.Streaming.Services;
using ActualChat.Testing.Host;
using ActualChat.Video;
using ActualLab.Rpc;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class VideoAdmissionTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Theory(Timeout = 30_000)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ReceptionShouldNegotiateBeforeDeliveringFrames(bool canDecodeHevc, bool isCapabilityAware)
    {
        // arrange
        var (session, chatId, streamId) = await CreateStream();
        var liveBackend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var streams = AppHost.Services.GetRequiredService<ILiveVideoStreams>();
        var decoderCodecs = canDecodeHevc
            ? new ApiArray<string>(["hevc", "vp9"])
            : new ApiArray<string>(["vp9"]);

        // act
        if (!isCapabilityAware)
            await streams.RegisterMember(session, chatId, decoderCodecs, CancellationToken.None);

        var stream = isCapabilityAware
            ? await streams.GetStreamWithCapabilities(session, streamId, decoderCodecs, CancellationToken.None)
            : await streams.GetStream(session, streamId, CancellationToken.None);
        stream.Should().NotBeNull();
        var received = await stream!.ToListAsync();
        var negotiated = await liveBackend.GetSupportedCodecs(chatId, CancellationToken.None);
        var count = await liveBackend.GetVideoStreamMemberCount(chatId, CancellationToken.None);

        // assert
        if (canDecodeHevc) {
            received.Should().NotBeEmpty();
            negotiated.Should().Contain("hevc");
        }
        else {
            received.Should().BeEmpty("unsupported keyframes must never enter the decoder pipeline");
            negotiated.Should().Equal("vp9");
        }
        count.Should().Be(1, "the reception lease survives EOF briefly while the sender replaces its stream");
    }

    [Fact(Timeout = 60_000)]
    public async Task ReceiverReleaseShouldRetryTransientFailures()
    {
        // arrange
        var (session, chatId, streamId) = await CreateStream();
        var services = AppHost.Services;
        var liveBackend = services.GetRequiredService<ILiveVideoBackend>();
        var backend = new Mock<ILiveVideoBackend>(MockBehavior.Strict);
        backend.Setup(x => x.List(It.IsAny<ChatId>(), It.IsAny<CancellationToken>()))
            .Returns((ChatId id, CancellationToken ct) => liveBackend.List(id, ct));
        backend.Setup(x => x.RegisterReceiver(
                It.IsAny<ChatId>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ApiArray<string>>(), It.IsAny<NodeRef>(), It.IsAny<CancellationToken>()))
            .Returns((
                ChatId id,
                string receiverId,
                string sessionId,
                ApiArray<string> codecs,
                NodeRef nodeRef,
                CancellationToken ct) => liveBackend.RegisterReceiver(id, receiverId, sessionId, codecs, nodeRef, ct));
        var cleanupAttempts = 0;
        backend.Setup(x => x.UnregisterMember(It.IsAny<ChatId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((ChatId id, string receiverId, CancellationToken ct) => {
                if (Interlocked.Increment(ref cleanupAttempts) == 1)
                    return Task.FromException(new InvalidOperationException("Transient receiver cleanup failure"));

                return liveBackend.UnregisterMember(id, receiverId, ct);
            });
        var streams = CreateStreams(backend.Object);

        // act
        var stream = await streams.GetStreamWithCapabilities(
            session, streamId, new ApiArray<string>(["vp9"]), CancellationToken.None);
        stream.Should().NotBeNull();
        _ = await stream!.ToListAsync();

        // assert
        await TestWait.When(async ct =>
            (await liveBackend.GetVideoStreamMemberCount(chatId, ct)).Should().Be(0), TimeSpan.FromSeconds(30));
        cleanupAttempts.Should().BeGreaterThanOrEqualTo(2);
    }

    [Theory(Timeout = 30_000)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OlderBackendShouldRetainSafeAdmission(bool isCapabilityAware)
    {
        // arrange
        var (session, chatId, streamId) = await CreateStream();
        var liveBackend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var backend = new Mock<ILiveVideoBackend>(MockBehavior.Strict);
        backend.Setup(x => x.List(It.IsAny<ChatId>(), It.IsAny<CancellationToken>()))
            .Returns((ChatId id, CancellationToken ct) => liveBackend.List(id, ct));
        backend.Setup(x => x.GetMemberCodecs(It.IsAny<ChatId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException("Endpoint not found: 'LiveVideoBackend.GetMemberCodecs:2'."));
        backend.Setup(x => x.RegisterReceiver(
                It.IsAny<ChatId>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<ApiArray<string>>(), It.IsAny<NodeRef>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RpcException("Endpoint not found: 'LiveVideoBackend.RegisterReceiver:5'."));
        backend.Setup(x => x.RegisterMember(
                It.IsAny<ChatId>(), It.IsAny<string>(), It.IsAny<ApiArray<string>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((
                ChatId id,
                string receiverId,
                ApiArray<string> codecs,
                bool isAdmin,
                CancellationToken ct) => liveBackend.RegisterMember(id, receiverId, codecs, isAdmin, ct));
        backend.Setup(x => x.UnregisterMember(It.IsAny<ChatId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((ChatId id, string receiverId, CancellationToken ct)
                => liveBackend.UnregisterMember(id, receiverId, ct));
        var streams = CreateStreams(backend.Object);

        // act
        var stream = isCapabilityAware
            ? await streams.GetStreamWithCapabilities(
                session, streamId, new ApiArray<string>(["hevc", "vp9"]), CancellationToken.None)
            : await streams.GetStream(session, streamId, CancellationToken.None);
        stream.Should().NotBeNull();
        var received = await stream!.ToListAsync();
        var count = await liveBackend.GetVideoStreamMemberCount(chatId, CancellationToken.None);

        // assert
        received.Should().HaveCount(isCapabilityAware ? 1 : 0);
        count.Should().Be(1);
    }

    private LiveVideoStreams CreateStreams(ILiveVideoBackend backend)
    {
        var provider = new Mock<IServiceProvider>(MockBehavior.Strict);
        provider.Setup(x => x.GetService(It.IsAny<Type>()))
            .Returns((Type type) => type == typeof(ILiveVideoBackend) ? backend : AppHost.Services.GetService(type));
        return new LiveVideoStreams(provider.Object);
    }

    private async Task<(Session Session, ChatId ChatId, StreamId StreamId)> CreateStream()
    {
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("VideoAdmission"));
        var chat = await Commander.Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff { Title = "VideoAdmission", Kind = ChatKind.Group },
            },
        });
        chat.Require();
        var services = AppHost.Services;
        var videoBackend = services.GetRequiredService<IVideoStreamingBackend>();
        var streamId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var record = new VideoRecord(
            streamId, session, chat.Id, Clocks.SystemClock.Now.EpochOffset.TotalSeconds,
            new VideoFormat { Codec = "hvc1", Size = new Size2D(320, 180) });
        var frame = new VideoFrame {
            Data = new byte[64], Index = 1, KeyFrameIndex = 1, Codec = "hvc1",
            Width = 320, Height = 180, Duration = TimeSpan.FromMilliseconds(33),
        };
        await videoBackend.PushVideo(record,
            new RpcStream<VideoFrameBundle>(new[] { new VideoFrameBundle([frame]) }.ToAsyncEnumerable()),
            CancellationToken.None);
        return (session, chat.Id, streamId);
    }
}
