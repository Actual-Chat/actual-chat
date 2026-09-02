using ActualChat.Testing.Host;

namespace ActualChat.Chat.IntegrationTests;

// GetTileNonComputed exists so a scan can read tiles without each one becoming a tracked,
// invalidated compute slot. These tests pin exactly that: same data, no captured computed.

[Collection(nameof(ChatCollection))]
public sealed class ChatTileNonComputedTest(ChatCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private ChatId TestChatId { get; } = ChatId.Parse("the-actual-one");

    [Fact]
    public async Task BackendShouldReturnTheSameTileAsGetTile()
    {
        // arrange
        var chatsBackend = AppHost.Services.GetRequiredService<IChatsBackend>();
        var tileRange = await GetTailTileRange(chatsBackend);

        // act
        var tile = await chatsBackend.GetTile(TestChatId, tileRange, false, CancellationToken.None);
        var nonComputedTile = await chatsBackend
            .GetTileNonComputed(TestChatId, tileRange, false, CancellationToken.None);

        // assert
        nonComputedTile.LidTileRange.Should().Be(tile.LidTileRange);
        nonComputedTile.Entries.Select(e => e.LocalId).Should().Equal(tile.Entries.Select(e => e.LocalId));
    }

    [Fact]
    public async Task BackendShouldCaptureNoComputed()
    {
        // arrange
        var chatsBackend = AppHost.Services.GetRequiredService<IChatsBackend>();
        var tileRange = await GetTailTileRange(chatsBackend);

        // act
        // The control: GetTile is a compute method, so it is capturable.
        var cTile = await Computed.Capture(
            () => chatsBackend.GetTile(TestChatId, tileRange, false, CancellationToken.None));
        var capture = () => Computed
            .Capture(() => chatsBackend.GetTileNonComputed(TestChatId, tileRange, false, CancellationToken.None))
            .AsTask();

        // assert
        cTile.Value.LidTileRange.Should().Be(tileRange);
        await capture.Should()
            .ThrowAsync<InvalidOperationException>("a non-compute call must leave nothing to depend on");
    }

    [Fact]
    public async Task ShouldReturnTheSameTileAsGetTile()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsBob();
        var session = tester.Session;
        var chats = tester.AppServices.GetRequiredService<IChats>();
        await tester.AppServices.GetRequiredService<IAuthors>()
            .EnsureJoined(session, TestChatId, CancellationToken.None);
        var tileRange = await GetTailTileRange(chats, session);

        // act
        var tile = await chats.GetTile(session, TestChatId, tileRange, CancellationToken.None);
        var nonComputedTile = await chats.GetTileNonComputed(session, TestChatId, tileRange, CancellationToken.None);

        // assert
        nonComputedTile.LidTileRange.Should().Be(tile.LidTileRange);
        nonComputedTile.Entries.Select(e => e.LocalId).Should().Equal(tile.Entries.Select(e => e.LocalId));
    }

    [Fact]
    public async Task ShouldRequireReadPermission()
    {
        // arrange
        await using var tester = AppHost.NewWebClientTester(Out);
        await tester.SignInAsBob();
        var session = tester.Session;
        var chats = tester.AppServices.GetRequiredService<IChats>();
        var chatId = ChatId.Parse(GroupChatId.New().Value);
        var tileRange = Constants.Chat.EntryIdTiles.GetTile(0L).Range;

        // act
        var act = () => chats.GetTileNonComputed(session, chatId, tileRange, CancellationToken.None);

        // assert
        await act.Should()
            .ThrowAsync<NotFoundException>("the non-compute path must run the same permission check");
    }

    // Private methods

    private async Task<Range<long>> GetTailTileRange(IChatsBackend chatsBackend)
    {
        var lidRange = await chatsBackend.GetLidRange(TestChatId, false, CancellationToken.None);
        return Constants.Chat.EntryIdTiles.GetTile(Math.Max(0, lidRange.End - 1)).Range;
    }

    private async Task<Range<long>> GetTailTileRange(IChats chats, Session session)
    {
        var lidRange = await chats.GetIdRange(session, TestChatId, CancellationToken.None);
        return Constants.Chat.EntryIdTiles.GetTile(Math.Max(0, lidRange.End - 1)).Range;
    }
}
