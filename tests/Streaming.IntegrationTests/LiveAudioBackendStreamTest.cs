using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Testing.Host;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class LiveAudioBackendStreamTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    /// <summary>
    /// Tests that ILiveAudioBackend.List works - registers a stream and verifies it appears in List.
    /// </summary>
    [Fact]
    public async Task List_ShouldReturnRegisteredStreams()
    {
        var services = AppHost.Services;
        var log = services.LogFor<LiveAudioBackendStreamTest>();

        // Create a test chat
        var commander = services.Commander();
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("StreamTester"));

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "LiveAudioBackendStreamTest",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();
        var chatId = chat.Id;

        log.LogInformation("Created chat {ChatId}", chatId);

        // Get the backend service
        var liveBackend = services.GetRequiredService<ILiveAudioBackend>();

        // Verify no streams initially
        var initialStreams = await liveBackend.List(chatId, CancellationToken.None);
        initialStreams.Count.Should().Be(0, "no streams should exist initially");

        // Register a stream
        var testStreamInfo = new LiveAudioStreamInfo {
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 1),
            StreamId = StreamId.New(services.MeshWatcher().ThisNode.Ref).Value,
            BeginsAt = SystemClock.Instance.Now,
            Format = AudioSource.DefaultFormat,
        };

        log.LogInformation("Registering active stream: #{StreamId}", testStreamInfo.StreamId);
        await liveBackend.Register(chatId, testStreamInfo, CancellationToken.None);

        // Verify stream appears in List
        var streams = await liveBackend.List(chatId, CancellationToken.None);
        log.LogInformation("Total streams: {Count}", streams.Count);
        streams.Should().ContainSingle("should have exactly one registered stream");
        streams[0].StreamId.Should().Be(testStreamInfo.StreamId);
        streams[0].ChatId.Should().Be(chatId);
    }

    /// <summary>
    /// Tests that computed List invalidation works - watches for changes using Computed.Capture.
    /// </summary>
    [Fact]
    public async Task List_ShouldInvalidateOnRegister()
    {
        var services = AppHost.Services;
        var log = services.LogFor<LiveAudioBackendStreamTest>();

        // Create a test chat
        var commander = services.Commander();
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("StreamTester2"));

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "LiveAudioBackendStreamTest2",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();
        var chatId = chat.Id;

        var liveBackend = services.GetRequiredService<ILiveAudioBackend>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Capture computed state
        var computed = await Computed.Capture(() => liveBackend.List(chatId, cts.Token));
        computed.Value.Count.Should().Be(0, "no streams initially");

        // Register a stream
        var testStreamInfo = new LiveAudioStreamInfo {
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 1),
            StreamId = StreamId.New(services.MeshWatcher().ThisNode.Ref).Value,
            BeginsAt = SystemClock.Instance.Now,
            Format = AudioSource.DefaultFormat,
        };

        log.LogInformation("Registering stream: #{StreamId}", testStreamInfo.StreamId);
        await liveBackend.Register(chatId, testStreamInfo, CancellationToken.None);

        // Wait for invalidation
        log.LogInformation("Waiting for invalidation...");
        await computed.WhenInvalidated(cts.Token);

        // Re-fetch and verify
        var updated = await Computed.Capture(() => liveBackend.List(chatId, cts.Token));
        updated.Value.Should().ContainSingle("registered stream should appear after invalidation");
        updated.Value[0].StreamId.Should().Be(testStreamInfo.StreamId);
    }

    /// <summary>
    /// Tests that multiple concurrent List consumers see the same data.
    /// </summary>
    [Fact]
    public async Task List_MultipleConcurrentConsumers()
    {
        var services = AppHost.Services;
        var log = services.LogFor<LiveAudioBackendStreamTest>();

        // Create a test chat
        var commander = services.Commander();
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("StreamTester3"));

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "LiveAudioBackendStreamTest3",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();
        var chatId = chat.Id;

        var liveBackend = services.GetRequiredService<ILiveAudioBackend>();

        // Register a stream
        var testStreamInfo = new LiveAudioStreamInfo {
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 1),
            StreamId = StreamId.New(services.MeshWatcher().ThisNode.Ref).Value,
            BeginsAt = SystemClock.Instance.Now,
            Format = AudioSource.DefaultFormat,
        };

        log.LogInformation("Registering stream: #{StreamId}", testStreamInfo.StreamId);
        await liveBackend.Register(chatId, testStreamInfo, CancellationToken.None);

        // Both calls should see the same data
        var list1 = await liveBackend.List(chatId, CancellationToken.None);
        var list2 = await liveBackend.List(chatId, CancellationToken.None);

        list1.Should().ContainSingle();
        list2.Should().ContainSingle();
        list1[0].StreamId.Should().Be(testStreamInfo.StreamId);
        list2[0].StreamId.Should().Be(testStreamInfo.StreamId);
    }

    /// <summary>
    /// Tests that computed List invalidates on unregister, proving the
    /// Computed.Capture + WhenInvalidated pattern works for stream removal.
    /// </summary>
    [Fact]
    public async Task List_ShouldInvalidateOnUnregister()
    {
        var services = AppHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await AppHost.SignIn(session, new AccountFull("StreamTester4"));

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "LiveAudioBackendStreamTest4",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();
        var chatId = chat.Id;

        var liveBackend = services.GetRequiredService<ILiveAudioBackend>();

        // Register a stream
        var streamInfo = new LiveAudioStreamInfo {
            ChatId = chatId,
            AuthorId = AuthorId.New(chatId, 1),
            StreamId = StreamId.New(services.MeshWatcher().ThisNode.Ref).Value,
            BeginsAt = SystemClock.Instance.Now,
            Format = AudioSource.DefaultFormat,
        };
        await liveBackend.Register(chatId, streamInfo, CancellationToken.None);

        // Capture computed with the stream present
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var computed = await Computed.Capture(() => liveBackend.List(chatId, cts.Token));
        computed.Value.Should().ContainSingle();

        // Unregister the stream
        await liveBackend.Unregister(chatId, streamInfo.StreamId, CancellationToken.None);

        // Computed should be invalidated
        computed.IsConsistent().Should().BeFalse("unregister should invalidate List computed");

        // Updated value should be empty
        var updated = await computed.Update(CancellationToken.None);
        updated.Value.Should().BeEmpty("stream should be gone after unregister");
    }
}
