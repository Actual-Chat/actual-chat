using ActualChat.Live;
using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Rpc;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ListeningStreamProcessorTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly TimeSpan ReconnectWaitTimeout = TimeSpan.FromSeconds(10).CiScaled();
    private static readonly ChatId TestChatId = ChatId.Parse("aaaaaaaaaaaaaaaaaaaa");
    private static readonly Session TestSession = Session.New();
    private static readonly AuthorId TestAuthorId = AuthorId.New(TestChatId, 1);

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

    [Fact]
    public async Task FramesArrivingFarBehindShouldResubscribeAtTheLiveEdge()
    {
        // arrange
        var lag = Constants.Audio.ListeningMaxArrivalLag + TimeSpan.FromSeconds(1);
        var (services, catchUpFroms) = CreateServices(ct => HangingStream(Moment.Now - lag, ct));
        var processor = new ListeningStreamProcessor(services, TestSession, TestChatId);

        // act
        _ = processor.Run();
        await WaitForConnections(catchUpFroms, 2);
        await processor.DisposeAsync();

        // assert
        catchUpFroms.Take(2).Should().Equal([default, default],
            "a receiver that fell behind must re-anchor at the live edge, not ask for a catch-up");
    }

    [Fact]
    public async Task FreshFramesShouldNotResubscribe()
    {
        // arrange
        var (services, catchUpFroms) = CreateServices(ct => HangingStream(Moment.Now, ct));
        var processor = new ListeningStreamProcessor(services, TestSession, TestChatId);

        // act
        _ = processor.Run();
        await WaitForConnections(catchUpFroms, 1);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await processor.DisposeAsync();

        // assert
        catchUpFroms.Should().HaveCount(1);
    }

    [Fact]
    public async Task CatchUpTargetShouldTolerateTheReplayLag()
    {
        // arrange
        var anchor = Moment.Now - TimeSpan.FromSeconds(30);
        var beginsAt = anchor + TimeSpan.FromSeconds(1); // a target: began at or after the anchor
        var (services, catchUpFroms) = CreateServices(ct => HangingStream(beginsAt, ct));
        var processor = new ListeningStreamProcessor(services, TestSession, TestChatId, anchor);

        // act
        _ = processor.Run();
        await WaitForConnections(catchUpFroms, 1);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await processor.DisposeAsync();

        // assert
        catchUpFroms.Should().HaveCount(1,
            "the server serves catch-up targets from t=0 on purpose, so their lag is not a stall");
    }

    [Fact]
    public async Task NonTargetStreamOnACatchUpConnectionShouldStillBeJudged()
    {
        // arrange
        var anchor = Moment.Now - TimeSpan.FromSeconds(30);
        var beginsAt = anchor - TimeSpan.FromSeconds(10); // pre-existing: served from the live edge
        var (services, catchUpFroms) = CreateServices(ct => HangingStream(beginsAt, ct));
        var processor = new ListeningStreamProcessor(services, TestSession, TestChatId, anchor);

        // act
        _ = processor.Run();
        await WaitForConnections(catchUpFroms, 2);
        await processor.DisposeAsync();

        // assert
        catchUpFroms.Take(2).Should().Equal([anchor, default],
            "a wake's first connection can last the whole session, so its live streams need the watchdog too");
    }

    [Fact]
    public async Task OwnStreamShouldNeverTriggerAResubscribe()
    {
        // arrange
        var lag = Constants.Audio.ListeningMaxArrivalLag + TimeSpan.FromSeconds(1);
        var (services, catchUpFroms) = CreateServices(ct => HangingStream(Moment.Now - lag, ct));
        var processor = new ListeningStreamProcessor(services, TestSession, TestChatId) {
            OwnAuthorId = TestAuthorId,
        };

        // act
        _ = processor.Run();
        await WaitForConnections(catchUpFroms, 1);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await processor.DisposeAsync();

        // assert
        catchUpFroms.Should().HaveCount(1, "own frames echo back late when the uplink is slow, not the downlink");
    }

    [Fact]
    public async Task UnsyncedServerClockShouldNotTriggerAResubscribe()
    {
        // arrange
        var lag = Constants.Audio.ListeningMaxArrivalLag + TimeSpan.FromSeconds(1);
        var (services, catchUpFroms) = CreateServices(ct => HangingStream(Moment.Now - lag, ct), isClockReady: false);
        var processor = new ListeningStreamProcessor(services, TestSession, TestChatId);

        // act
        _ = processor.Run();
        await WaitForConnections(catchUpFroms, 1);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await processor.DisposeAsync();

        // assert
        catchUpFroms.Should().HaveCount(1, "before the first sync ServerClock.Now is the device clock");
    }

    // Private methods

    private (IServiceProvider Services, List<Moment> CatchUpFroms) CreateServices(
        Func<CancellationToken, IAsyncEnumerable<MuxedAudioStreamItem>>? streamFactory = null,
        bool isClockReady = true)
    {
        // A fresh ServerClock reads the base clock until its first sync; the watchdog waits for that.
        var clocks = new MomentClockSet(MomentClockSet.Default.SystemClock);
        if (isClockReady)
            clocks.ServerClock.Offset = TimeSpan.Zero;
        // An ending stream is a transient drop to an infinite ResilientStream: it reconnects
        streamFactory ??= _ => AsyncEnumerable.Empty<MuxedAudioStreamItem>();
        var catchUpFroms = new List<Moment>();
        var liveStreams = new Mock<ILiveAudioStreams>(MockBehavior.Strict);
        liveStreams
            .Setup(x => x.GetListeningStream(
                It.IsAny<Session>(), It.IsAny<ChatId>(), It.IsAny<Moment>(), It.IsAny<CancellationToken>()))
            .Returns((Session _, ChatId _, Moment catchUpFrom, CancellationToken ct) => {
                lock (catchUpFroms)
                    catchUpFroms.Add(catchUpFrom);
                return Task.FromResult(RpcStream.New(streamFactory.Invoke(ct)));
            });
        var services = new ServiceCollection()
            .AddTestLogging(Out)
            .AddSingleton(clocks)
            .AddSingleton(liveStreams.Object)
            .BuildServiceProvider();
        return (services, catchUpFroms);
    }

    // One stream whose frames claim to have been captured at beginsAt, then silence: the
    // stream never ends, so only a Break() can produce a second connection.
    private static async IAsyncEnumerable<MuxedAudioStreamItem> HangingStream(
        Moment beginsAt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new MuxedAudioStreamStart {
            StreamIndex = 1,
            StreamInfo = new LiveAudioStreamInfo {
                ChatId = TestChatId,
                AuthorId = TestAuthorId,
                StreamId = "s1",
                BeginsAt = beginsAt,
            },
        };
        yield return new MuxedAudioFrame { StreamIndex = 1, Offset = TimeSpan.Zero, Data = new byte[1] };
        yield return new MuxedAudioFrame {
            StreamIndex = 1,
            Offset = TimeSpan.FromMilliseconds(20),
            Data = new byte[1],
        };
        await TaskExt.NeverEnding(cancellationToken);
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
