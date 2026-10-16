using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ChatRangeTileStandInTest
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    // An older server merged the next cid tile into the cached tail tile: the collapsed conversation counts
    // as one row, so the tile at 1280 held too few of them. The chat has grown by 40 entries since.
    private static readonly Range<long> Conversation = new(2162, 2851);
    private static readonly ChatRangeTile CachedTail
        = new(new(1280, 2851), [new(1281, 2851)], [Conversation], 60, 0, null);
    private static readonly Range<long> QueriedCidTiles = new(1280, 2560);
    private static readonly Range<long> ChatLidRange = new(1, 2891);

    [Fact]
    public void StandInShouldSynthesizeTheTailUpToTheChatEnd()
    {
        // act
        var tiles = ChatRangeTileCache.StandIn([CachedTail], QueriedCidTiles, ChatLidRange);

        // assert
        tiles.Should().NotBeNull();
        tiles![^1].LidRange.End.Should().Be(ChatLidRange.End);
        tiles[^1].EntryLidRanges[^1].End.Should().Be(ChatLidRange.End);
        tiles[^1].NextLidTileStart.Should().BeNull();
    }

    [Fact]
    public void NavigationBuildFromStandInShouldReachTheNewEntries()
    {
        // arrange
        var tiles = ChatRangeTileCache.StandIn([CachedTail], QueriedCidTiles, ChatLidRange)!;
        var view = new ConversationViewState(
            false, ImmutableHashSet<ConversationId>.Empty, default, null, default, null);
        var conversationId = ConversationId.New(TestChatId, Conversation.Start);
        var blocks = new ChatBlock[] {
            new(new(conversationId) { EndEntryLid = Conversation.End - 1 }, Conversation, Moment.EpochStart),
        };
        var query = new ChatDataQuery(new(2160, 2165), -26, 53);

        // act
        var isFulfilled = ChatUI.TryGetIdTilesToLoad(view, blocks, query, tiles,
            out var idTiles, out _, out var hasAfter);

        // assert
        isFulfilled.Should().BeTrue();
        idTiles.Should().Contain(ChatUI.EntryIdTiles.GetTile(Conversation.Start).Range);
        idTiles.Should().Contain(ChatUI.EntryIdTiles.GetTile(ChatLidRange.End - 1).Range);
        hasAfter.Should().BeFalse();
    }

    [Fact]
    public void StandInBelowTheCachedSpanShouldStillAwaitTheFreshMeta()
    {
        // arrange
        var cached = new ChatRangeTile(new(1280, 2560), [new(1281, 2560)], [], 100, 0, 2560);

        // act
        var tiles = ChatRangeTileCache.StandIn([cached], new(1280, 3840), ChatLidRange);

        // assert
        tiles.Should().BeNull();
    }
}
