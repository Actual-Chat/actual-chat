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
        var streamId = StreamId.New(new NodeRef(Generate.Option));
        var streamInfo = new VideoStreamInfo(
            streamId,
            chatId,
            bobAuthor!.Id,
            new VideoFormat { Codec = "avc1", Width = 640, Height = 480 },
            Clocks.SystemClock.Now);

        // Act: register the video stream
        await backend.RegisterActiveStream(chatId, streamInfo, CancellationToken.None);

        // Assert: GetVideoStreamingAuthorIds should return Bob's AuthorId
        var authorIds = await backend.GetVideoStreamingAuthorIds(chatId, CancellationToken.None);
        authorIds.Should().HaveCount(1);
        authorIds.Should().Contain(bobAuthor.Id);

        // Assert: ListActiveStreams should include the stream
        var activeStreams = await backend.ListActiveStreams(chatId, CancellationToken.None);
        activeStreams.Should().HaveCount(1);
        activeStreams[0].StreamId.Should().Be(streamId);
        activeStreams[0].AuthorId.Should().Be(bobAuthor.Id);
    }

    [Fact]
    public async Task ShouldInvalidateStreamingAuthorIdsOnChange()
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
            () => backend.GetVideoStreamingAuthorIds(chatId, CancellationToken.None));
        computed.Value.Should().BeEmpty();
        computed.IsConsistent().Should().BeTrue();

        // Register a video stream
        var streamId = StreamId.New(new NodeRef(Generate.Option));
        var streamInfo = new VideoStreamInfo(
            streamId,
            chatId,
            bobAuthor!.Id,
            new VideoFormat { Codec = "avc1", Width = 640, Height = 480 },
            Clocks.SystemClock.Now);

        await backend.RegisterActiveStream(chatId, streamInfo, CancellationToken.None);

        // Computed should be invalidated
        computed.IsConsistent().Should().BeFalse();

        // Re-capture — should now return the streaming author
        computed = await computed.Update(CancellationToken.None);
        computed.Value.Should().HaveCount(1);
        computed.Value.Should().Contain(bobAuthor.Id);

        // Unregister the stream
        await backend.UnregisterActiveStream(chatId, streamId, CancellationToken.None);

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
        var authorIds = await frontend.GetVideoStreamingAuthorIds(alice.Session, chatId, CancellationToken.None);
        authorIds.Should().BeEmpty();

        // Register Bob's video stream via backend
        var streamId = StreamId.New(new NodeRef(Generate.Option));
        var streamInfo = new VideoStreamInfo(
            streamId,
            chatId,
            bobAuthor!.Id,
            new VideoFormat { Codec = "avc1", Width = 640, Height = 480 },
            Clocks.SystemClock.Now);

        await backend.RegisterActiveStream(chatId, streamInfo, CancellationToken.None);

        // Alice should see Bob's stream via frontend service
        authorIds = await frontend.GetVideoStreamingAuthorIds(alice.Session, chatId, CancellationToken.None);
        authorIds.Should().HaveCount(1);
        authorIds.Should().Contain(bobAuthor.Id);
    }
}
