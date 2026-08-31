using ActualChat.Streaming;
using ActualChat.Testing.Host;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class VideoStreamMemberCountTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task ShouldTrackStreamMemberCount()
    {
        // Arrange: create two users and a chat
        await using var bob = AppHost.NewWebClientTester(Out);
        await using var alice = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();
        await alice.SignInAsUniqueAlice();

        var (chatId, inviteId) = await bob.CreateChat(false);
        await alice.JoinChat(chatId, inviteId);

        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var bobSessionId = bob.Session.Id;
        var aliceSessionId = alice.Session.Id;

        // Initially zero
        var count = await backend.GetVideoStreamMemberCount(chatId, CancellationToken.None);
        count.Should().Be(0);

        // Register Bob as stream member
        await backend.RegisterMember(chatId, bobSessionId, new ApiArray<string>(["h264", "av1"]), false, CancellationToken.None);
        count = await backend.GetVideoStreamMemberCount(chatId, CancellationToken.None);
        count.Should().Be(1);

        // Register Alice as stream member
        await backend.RegisterMember(chatId, aliceSessionId, new ApiArray<string>(["h264", "av1"]), false, CancellationToken.None);
        count = await backend.GetVideoStreamMemberCount(chatId, CancellationToken.None);
        count.Should().Be(2);

        // Duplicate registration of Bob should not increase count
        await backend.RegisterMember(chatId, bobSessionId, new ApiArray<string>(["h264", "av1"]), false, CancellationToken.None);
        count = await backend.GetVideoStreamMemberCount(chatId, CancellationToken.None);
        count.Should().Be(2);

        // Unregister Bob
        await backend.UnregisterMember(chatId, bobSessionId, CancellationToken.None);
        count = await backend.GetVideoStreamMemberCount(chatId, CancellationToken.None);
        count.Should().Be(1);

        // Unregister Alice
        await backend.UnregisterMember(chatId, aliceSessionId, CancellationToken.None);
        count = await backend.GetVideoStreamMemberCount(chatId, CancellationToken.None);
        count.Should().Be(0);
    }

    [Fact]
    public async Task ShouldNotCountAcrossChats()
    {
        // Arrange: create a user with two chats
        await using var bob = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();

        var (chatId1, _) = await bob.CreateChat(true, "Chat 1");
        var (chatId2, _) = await bob.CreateChat(true, "Chat 2");

        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var bobSessionId = bob.Session.Id;

        // Register Bob in chat 1 only
        await backend.RegisterMember(chatId1, bobSessionId, new ApiArray<string>(["h264", "av1"]), false, CancellationToken.None);

        var count1 = await backend.GetVideoStreamMemberCount(chatId1, CancellationToken.None);
        var count2 = await backend.GetVideoStreamMemberCount(chatId2, CancellationToken.None);

        count1.Should().Be(1);
        count2.Should().Be(0);
    }

    [Fact]
    public async Task ShouldInvalidateComputedOnChange()
    {
        // Arrange
        await using var bob = AppHost.NewWebClientTester(Out);
        await bob.SignInAsUniqueBob();

        var (chatId, _) = await bob.CreateChat(true);

        var backend = AppHost.Services.GetRequiredService<ILiveVideoBackend>();
        var bobSessionId = bob.Session.Id;

        // Capture initial computed value
        var computed = await Computed.Capture(
            () => backend.GetVideoStreamMemberCount(chatId, CancellationToken.None));
        computed.Value.Should().Be(0);
        computed.IsConsistent().Should().BeTrue();

        // Register a member — computed should become inconsistent
        await backend.RegisterMember(chatId, bobSessionId, new ApiArray<string>(["h264", "av1"]), false, CancellationToken.None);
        computed.IsConsistent().Should().BeFalse();

        // Re-capture — should reflect new value
        computed = await computed.Update(CancellationToken.None);
        computed.Value.Should().Be(1);
        computed.IsConsistent().Should().BeTrue();
    }
}
