using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Rpc;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ListeningStreamProcessorTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly TimeSpan ReconnectWaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly ChatId TestChatId = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly Session TestSession = Session.New();

    [Fact]
    public async Task CatchUpAnchorShouldReachTheFirstConnectionOnly()
    {
        // arrange
        var anchor = Moment.Now - TimeSpan.FromSeconds(5);
        var (services, catchUpFroms) = CreateServices();
        var processor = new ListeningStreamProcessor(services, TestSession, TestChatId, anchor);

        // act
        _ = processor.Run();
        await WaitForConnections(catchUpFroms, 2);
        await processor.DisposeAsync();

        // assert
        catchUpFroms.Take(2).Should().Equal([anchor, default],
            "the server serves the trigger utterance from t=0 to whoever asks, so a reconnect must not ask again");
    }

    [Fact]
    public async Task StaleCatchUpAnchorShouldNeverReachTheServer()
    {
        // arrange
        var anchor = Moment.Now - Constants.Audio.PttStaleWakeAge - TimeSpan.FromSeconds(1);
        var (services, catchUpFroms) = CreateServices();
        var processor = new ListeningStreamProcessor(services, TestSession, TestChatId, anchor);

        // act
        _ = processor.Run();
        await WaitForConnections(catchUpFroms, 1);
        await processor.DisposeAsync();

        // assert
        catchUpFroms[0].Should().Be(default(Moment));
    }

    // Private methods

    private (IServiceProvider Services, List<Moment> CatchUpFroms) CreateServices()
    {
        var catchUpFroms = new List<Moment>();
        var liveStreams = new Mock<ILiveAudioStreams>(MockBehavior.Strict);
        liveStreams
            .Setup(x => x.GetListeningStream(
                It.IsAny<Session>(), It.IsAny<ChatId>(), It.IsAny<Moment>(), It.IsAny<CancellationToken>()))
            .Returns((Session _, ChatId _, Moment catchUpFrom, CancellationToken _) => {
                lock (catchUpFroms)
                    catchUpFroms.Add(catchUpFrom);
                // An ending stream is a transient drop to an infinite ResilientStream: it reconnects
                return Task.FromResult(RpcStream.New(AsyncEnumerable.Empty<MuxedAudioStreamItem>()));
            });
        var services = new ServiceCollection()
            .AddTestLogging(Out)
            .AddSingleton(liveStreams.Object)
            .BuildServiceProvider();
        return (services, catchUpFroms);
    }

    private static async Task WaitForConnections(List<Moment> catchUpFroms, int count)
    {
        using var cts = new CancellationTokenSource(ReconnectWaitTimeout);
        while (true) {
            lock (catchUpFroms)
                if (catchUpFroms.Count >= count)
                    return;

            await Task.Delay(50, cts.Token);
        }
    }
}
