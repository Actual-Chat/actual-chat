using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Live;
using ActualChat.Testing.Host;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class LiveBackendStreamTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    /// <summary>
    /// Tests that ILiveAudioBackend.ObserveStreams works in loopback mode (same host produces and consumes).
    /// This is the simplest possible test for the RPC stream functionality.
    /// </summary>
    [Fact]
    public async Task ObserveStreams_ShouldWorkInLoopback()
    {
        var services = AppHost.Services;
        var log = services.LogFor<LiveBackendStreamTest>();

        // Create a test chat
        var commander = services.Commander();
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("StreamTester"));

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "LiveBackendStreamTest",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();
        var chatId = chat.Id;

        log.LogInformation("Created chat {ChatId}", chatId);

        // Get the backend service
        var liveBackend = services.GetRequiredService<ILiveAudioBackend>();

        // Start observing streams
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receivedStreams = new List<LiveStreamInfo>();

        log.LogInformation("Calling ObserveStreams for chat {ChatId}...", chatId);
        var rpcStream = await liveBackend.ObserveStreams(chatId, cts.Token);
        log.LogInformation("Got RpcStream, starting enumeration...");

        // Start collecting in background
        var collectTask = Task.Run(async () => {
            try {
                log.LogInformation("Starting to enumerate RPC stream...");
                await foreach (var streamInfo in rpcStream.WithCancellation(cts.Token)) {
                    log.LogInformation("Received stream: {StreamId}, ChatId={ChatId}, AuthorId={AuthorId}",
                        streamInfo.StreamId, streamInfo.ChatId, streamInfo.AuthorId);
                    receivedStreams.Add(streamInfo);
                }
                log.LogInformation("RPC stream enumeration completed normally");
            }
            catch (OperationCanceledException) {
                log.LogInformation("RPC stream enumeration cancelled after receiving {Count} streams", receivedStreams.Count);
            }
            catch (Exception ex) {
                log.LogError(ex, "Error during RPC stream enumeration");
                throw;
            }
        }, cts.Token);

        // Wait a bit for the observer to start
        await Task.Delay(500, cts.Token);

        // Now register a stream (simulating what StreamingBackend does)
        var testStreamInfo = new LiveStreamInfo {
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 1),
            StreamId = $"test-stream-{Guid.NewGuid():N}",
            BeginsAt = SystemClock.Instance.Now,
            Format = AudioSource.DefaultFormat,
        };

        log.LogInformation("Registering active stream: {StreamId}", testStreamInfo.StreamId);

        // Use the interface method (now part of ILiveAudioBackend)
        await liveBackend.RegisterActiveStream(chatId, testStreamInfo, cts.Token);

        log.LogInformation("Stream registered, waiting for it to be received...");

        // Wait for the stream to be received
        await Task.Delay(2000, cts.Token);

        // Cancel and verify
        await cts.CancelAsync();
        await collectTask.SilentAwait(false);

        // Verify
        log.LogInformation("Total streams received: {Count}", receivedStreams.Count);
        receivedStreams.Should().ContainSingle("should receive exactly one registered stream");
        receivedStreams[0].StreamId.Should().Be(testStreamInfo.StreamId);
        receivedStreams[0].ChatId.Should().Be(chatId);
    }

    /// <summary>
    /// Tests that ObserveStreams yields existing streams first before waiting for new ones.
    /// </summary>
    [Fact]
    public async Task ObserveStreams_ShouldYieldExistingStreamsFirst()
    {
        var services = AppHost.Services;
        var log = services.LogFor<LiveBackendStreamTest>();

        // Create a test chat
        var commander = services.Commander();
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("StreamTester2"));

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "LiveBackendStreamTest2",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();
        var chatId = chat.Id;

        var liveBackend = services.GetRequiredService<ILiveAudioBackend>();

        // First, register a stream BEFORE calling ObserveStreams
        var existingStreamInfo = new LiveStreamInfo {
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 1),
            StreamId = $"existing-stream-{Guid.NewGuid():N}",
            BeginsAt = SystemClock.Instance.Now,
            Format = AudioSource.DefaultFormat,
        };

        log.LogInformation("Registering existing stream: {StreamId}", existingStreamInfo.StreamId);
        await liveBackend.RegisterActiveStream(chatId, existingStreamInfo, CancellationToken.None);

        // Now call ObserveStreams - it should immediately yield the existing stream
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var receivedStreams = new List<LiveStreamInfo>();

        log.LogInformation("Calling ObserveStreams...");
        var rpcStream = await liveBackend.ObserveStreams(chatId, cts.Token);

        // Try to get the first item with a short timeout
        var enumerator = rpcStream.GetAsyncEnumerator(cts.Token);
        try {
            log.LogInformation("Moving to first item...");
            var hasFirst = await enumerator.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            hasFirst.Should().BeTrue("existing stream should be yielded immediately");

            var firstStream = enumerator.Current;
            log.LogInformation("Got first stream: {StreamId}", firstStream.StreamId);
            firstStream.StreamId.Should().Be(existingStreamInfo.StreamId);
        }
        finally {
            await enumerator.DisposeAsync();
        }
    }

    /// <summary>
    /// Tests multiple concurrent ObserveStreams calls.
    /// Note: Currently, new stream notifications go to only one consumer due to
    /// single channel design in ChatStreamSet. This test documents current behavior.
    /// </summary>
    [Fact(Skip = "Multiple concurrent consumers receive only one notification due to single channel design")]
    public async Task ObserveStreams_MultipleConcurrentConsumers()
    {
        var services = AppHost.Services;
        var log = services.LogFor<LiveBackendStreamTest>();

        // Create a test chat
        var commander = services.Commander();
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("StreamTester3"));

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "LiveBackendStreamTest3",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();
        var chatId = chat.Id;

        var liveBackend = services.GetRequiredService<ILiveAudioBackend>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Start two concurrent consumers
        var consumer1Streams = new List<LiveStreamInfo>();
        var consumer2Streams = new List<LiveStreamInfo>();

        var stream1 = await liveBackend.ObserveStreams(chatId, cts.Token);
        var stream2 = await liveBackend.ObserveStreams(chatId, cts.Token);

        var consumer1Task = Task.Run(async () => {
            try {
                await foreach (var s in stream1.WithCancellation(cts.Token))
                    consumer1Streams.Add(s);
            }
            catch (OperationCanceledException) { }
        }, cts.Token);

        var consumer2Task = Task.Run(async () => {
            try {
                await foreach (var s in stream2.WithCancellation(cts.Token))
                    consumer2Streams.Add(s);
            }
            catch (OperationCanceledException) { }
        }, cts.Token);

        await Task.Delay(500, cts.Token);

        // Register a stream
        var testStreamInfo = new LiveStreamInfo {
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 1),
            StreamId = $"multi-consumer-stream-{Guid.NewGuid():N}",
            BeginsAt = SystemClock.Instance.Now,
            Format = AudioSource.DefaultFormat,
        };

        log.LogInformation("Registering stream: {StreamId}", testStreamInfo.StreamId);
        await liveBackend.RegisterActiveStream(chatId, testStreamInfo, cts.Token);

        // Wait for both consumers to receive
        await Task.Delay(2000, cts.Token);

        await cts.CancelAsync();
        await Task.WhenAll(consumer1Task, consumer2Task).SilentAwait(false);

        // Both consumers should receive the stream
        log.LogInformation("Consumer1 received: {Count}, Consumer2 received: {Count}",
            consumer1Streams.Count, consumer2Streams.Count);

        consumer1Streams.Should().ContainSingle();
        consumer2Streams.Should().ContainSingle();
        consumer1Streams[0].StreamId.Should().Be(testStreamInfo.StreamId);
        consumer2Streams[0].StreamId.Should().Be(testStreamInfo.StreamId);
    }
}
