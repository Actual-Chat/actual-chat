using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class LiveConversationDisplayTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldSurfaceVideoOnlyLiveBlockStartingAViewTile()
    {
        // The block sits at a lid only transcription ever fills, so video-only leaves its tile unloaded.

        // arrange
        await Tester.SignInAsUniqueBob();
        var (chat, _) = await Tester.CreateAndGetChat(false, "live-block-boundary-test");
        var tileSize = ChatUI.IdTileStack.FirstLayer.TileSize;
        ChatEntry entry;
        do {
            entry = await Tester.CreateTextEntry(chat.Id, "filler");
        } while ((entry.LocalId + 1) % tileSize != 0);
        var author = await Tester.GetOwnAuthor(chat.Id).Require();
        var peerId = AuthorId.New(chat.Id, 777_070);
        var liveBackend = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        // act
        await liveBackend.OnStreamRegistered(chat.Id, author.Id, null, false, CancellationToken.None);
        await liveBackend.OnStreamRegistered(chat.Id, peerId, null, false, CancellationToken.None);
        var live = await liveBackend.GetState(chat.Id, CancellationToken.None);
        var chatUI = Tester.ScopedAppServices.GetRequiredService<ChatUI>();
        var idRange = await Tester.Chats.GetIdRange(Tester.Session, chat.Id, CancellationToken.None);
        var query = new ChatDataQuery(idRange, -chatUI.HalfLoadLimit, chatUI.HalfLoadLimit);
        var items = await chatUI.GetChatItems(chat.Id, query, 0, CancellationToken.None);

        // assert
        live.Should().NotBeNull();
        live!.SessionStartedAt.Should().NotBeNull();
        (live.VisibleStartLid % tileSize).Should()
            .Be(0L, "the live block must start a fresh view tile or this test doesn't bite");
        items.Items.OfType<ConversationMessage>()
            .Select(m => m.Conversation!.Id)
            .Should().Contain(live.ConversationId);
    }
}
