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
    public async Task ReceptionShouldRespectExplicitCapabilitiesOrLegacyFloor(
        bool canDecodeHevc, bool isCapabilityAware)
    {
        // arrange
        var (session, chatId, streamId) = await CreateStream();
        var liveBackend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var streams = AppHost.Services.GetRequiredService<ILiveVideoStreams>();
        var decoderCodecs = canDecodeHevc
            ? new ApiArray<string>(["hevc", "vp9"])
            : new ApiArray<string>(["vp9"]);

        // act
        await streams.RegisterMember(session, chatId, decoderCodecs, CancellationToken.None);

        var stream = isCapabilityAware
            ? await streams.GetStreamWithCapabilities(session, streamId, decoderCodecs, CancellationToken.None)
            : await streams.GetStream(session, streamId, CancellationToken.None);
        stream.Should().NotBeNull();
        var received = await stream!.ToListAsync();
        var negotiated = await liveBackend.GetSupportedCodecs(chatId, CancellationToken.None);
        var count = await liveBackend.GetVideoStreamMemberCount(chatId, CancellationToken.None);

        // assert
        received.Should().HaveCount(canDecodeHevc && isCapabilityAware ? 1 : 0);
        negotiated.Should().Contain("vp9");
        negotiated.Contains("hevc").Should().Be(canDecodeHevc);
        count.Should().Be(1);
    }

    [Fact(Timeout = 30_000)]
    public async Task LegacyPullShouldAdmitVp9WithoutCreatingMembership()
    {
        // arrange
        var (session, chatId, streamId) = await CreateStream("vp09");
        var streams = AppHost.Services.GetRequiredService<ILiveVideoStreams>();
        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();

        // act
        var stream = await streams.GetStream(session, streamId, CancellationToken.None);
        stream.Should().NotBeNull();
        var received = await stream!.ToListAsync();
        var count = await backend.GetVideoStreamMemberCount(chatId, CancellationToken.None);

        // assert
        received.Should().ContainSingle();
        count.Should().Be(0);
    }

    [Fact(Timeout = 30_000)]
    public async Task PullShouldNotOverwriteAdminOverrideOrOwnMembership()
    {
        // arrange
        var (session, chatId, streamId) = await CreateStream();
        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var streams = AppHost.Services.GetRequiredService<ILiveVideoStreams>();
        await backend.RegisterMember(chatId, session.Id,
            new ApiArray<string>([LiveVideoBackend.ChatState.ForcedCodecMarker, "hevc"]),
            true, CancellationToken.None);

        // act
        var stream = await streams.GetStreamWithCapabilities(
            session, streamId, new ApiArray<string>(["vp9"]), CancellationToken.None);
        stream.Should().NotBeNull();
        var received = await stream!.ToListAsync();
        var negotiated = await backend.GetSupportedCodecs(chatId, CancellationToken.None);
        var count = await backend.GetVideoStreamMemberCount(chatId, CancellationToken.None);

        // assert
        received.Should().BeEmpty();
        negotiated.Should().Equal("hevc");
        count.Should().Be(1);
    }

    private async Task<(Session Session, ChatId ChatId, StreamId StreamId)> CreateStream(string codec = "hvc1")
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
            new VideoFormat { Codec = codec, Size = new Size2D(320, 180) });
        var frame = new VideoFrame {
            Data = new byte[64], Index = 1, KeyFrameIndex = 1, Codec = codec,
            Width = 320, Height = 180, Duration = TimeSpan.FromMilliseconds(33),
        };
        await videoBackend.PushVideo(record,
            new RpcStream<VideoFrameBundle>(new[] { new VideoFrameBundle([frame]) }.ToAsyncEnumerable()),
            CancellationToken.None);
        return (session, chat.Id, streamId);
    }
}
