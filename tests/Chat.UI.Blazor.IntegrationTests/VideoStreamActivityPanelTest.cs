using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.Video;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class VideoStreamActivityPanelTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ShouldDetectVideoStreamingAuthor()
    {
        // Arrange: create two users and a chat
        await using var bob = AppHost.NewWebClientTester(Out);
        await using var alice = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();

        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);

        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        bobAuthor.Should().NotBeNull();

        // Create a video stream for Bob
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);
        var streamInfo = new VideoStreamInfo(
            streamId,
            chatId,
            bobAuthor!.Id,
            new[] { new VideoFormat { Codec = "avc1", Size = new Size2D(640, 480) } },
            Clocks.SystemClock.Now);

        // Act: register the video stream
        await backend.Register(chatId, streamInfo, CancellationToken.None);

        // Assert: List should include the stream with Bob's AuthorId
        var activeStreams = await backend.List(chatId, CancellationToken.None);
        activeStreams.Should().HaveCount(1);
        activeStreams[0].StreamId.Should().Be(streamId);
        activeStreams[0].AuthorId.Should().Be(bobAuthor.Id);
    }

    [Fact]
    public async Task ShouldInvalidateListOnStreamChange()
    {
        // Arrange
        await using var bob = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();

        var (chatId, _) = await bob.CreateChat(true);

        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        bobAuthor.Should().NotBeNull();

        // Capture initial computed value — should be empty
        var computed = await Computed.Capture(
            () => backend.List(chatId, CancellationToken.None));
        computed.Value.Should().BeEmpty();
        computed.IsConsistent().Should().BeTrue();

        // Register a video stream
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);
        var streamInfo = new VideoStreamInfo(
            streamId,
            chatId,
            bobAuthor!.Id,
            new[] { new VideoFormat { Codec = "avc1", Size = new Size2D(640, 480) } },
            Clocks.SystemClock.Now);

        await backend.Register(chatId, streamInfo, CancellationToken.None);

        // Computed should be invalidated
        computed.IsConsistent().Should().BeFalse();

        // Re-capture — should now return the stream
        computed = await computed.Update(CancellationToken.None);
        computed.Value.Should().HaveCount(1);
        computed.Value[0].AuthorId.Should().Be(bobAuthor.Id);

        // Unregister the stream
        await backend.Unregister(chatId, streamId, CancellationToken.None);

        // Computed should be invalidated again
        computed.IsConsistent().Should().BeFalse();

        // Re-capture — should be empty again
        computed = await computed.Update(CancellationToken.None);
        computed.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldDetectVideoStreamingViaFrontendService()
    {
        // Arrange: this tests the full pipeline through ILiveVideoStreams (frontend)
        // which is what ChatVideoUI.IsAnyoneVideoStreaming calls
        await using var bob = AppHost.NewWebClientTester(Out);
        await using var alice = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();

        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);

        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var frontend = AppHost.Services.GetRequiredService<ILiveVideoStreams>();
        var bobAuthor = await bob.GetOwnAuthor(chatId);
        bobAuthor.Should().NotBeNull();

        // Initially no one is streaming
        var streams = await frontend.List(alice.Session, chatId, CancellationToken.None);
        streams.Should().BeEmpty();

        // Register Bob's video stream via backend
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);
        var streamInfo = new VideoStreamInfo(
            streamId,
            chatId,
            bobAuthor!.Id,
            new[] { new VideoFormat { Codec = "avc1", Size = new Size2D(640, 480) } },
            Clocks.SystemClock.Now);

        await backend.Register(chatId, streamInfo, CancellationToken.None);

        // Alice should see Bob's stream via frontend service
        streams = await frontend.List(alice.Session, chatId, CancellationToken.None);
        streams.Should().HaveCount(1);
        streams[0].AuthorId.Should().Be(bobAuthor.Id);
    }
}
