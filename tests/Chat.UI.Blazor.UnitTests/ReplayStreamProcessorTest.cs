using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Rpc;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ReplayStreamProcessorTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly Session TestSession = Session.New();

    [Fact]
    public async Task ADubLanguageShouldGoToTheDubbingOverload()
    {
        // arrange
        var liveStreams = new Mock<ILiveAudioStreams>(MockBehavior.Strict);
        liveStreams
            .Setup(x => x.GetReplayStream(It.IsAny<Session>(), It.IsAny<ChatId>(), It.IsAny<Moment>(), It.IsAny<TimeSpan>(), 1.0, Languages.English, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyStream());
        var processor = new ReplayStreamProcessor(Services(liveStreams.Object), TestSession, TestChatId, Moment.EpochStart, TimeSpan.Zero) {
            DubLanguageProvider = _ => Task.FromResult<Language?>(Languages.English),
        };

        // act
        await processor.Run();

        // assert
        liveStreams.VerifyAll();
    }

    [Fact]
    public async Task NoDubLanguageShouldKeepTheOldOverload()
    {
        // arrange
        var liveStreams = new Mock<ILiveAudioStreams>(MockBehavior.Strict);
        liveStreams
            .Setup(x => x.GetReplayStream(It.IsAny<Session>(), It.IsAny<ChatId>(), It.IsAny<Moment>(), It.IsAny<TimeSpan>(), 1.0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyStream());
        var processor = new ReplayStreamProcessor(Services(liveStreams.Object), TestSession, TestChatId, Moment.EpochStart, TimeSpan.Zero) {
            DubLanguageProvider = _ => Task.FromResult<Language?>(null),
        };

        // act
        await processor.Run();

        // assert
        liveStreams.VerifyAll();
    }

    // Private methods

    private IServiceProvider Services(ILiveAudioStreams liveStreams)
        => new ServiceCollection()
            .AddTestLogging(Out)
            .AddSingleton(liveStreams)
            .BuildServiceProvider();

    private static RpcStream<MuxedAudioStreamItem> EmptyStream()
        => RpcStream.New(AsyncEnumerable.Empty<MuxedAudioStreamItem>());
}
