using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ChatRangeTilePrefetchTest
{
    private static readonly long TileSize = ChatUI.ConversationIdTiles.TileSize;

    [Fact]
    public async Task SparseTailShouldWalkToPreviousTilesUntilEnoughRows()
    {
        // arrange
        var tiles = NewTiles(rowsPerTile: 30, count: 6);
        var requested = new List<long>();

        // act
        var result = await ChatUI.AddPreviousRangeTiles([tiles[^1]], start => Fetch(tiles, requested, start));

        // assert
        requested.Should().Equal(tiles[^2].LidRange.Start, tiles[^3].LidRange.Start);
        result.Select(m => m.LidRange.Start).Should().Equal(
            tiles[^3].LidRange.Start, tiles[^2].LidRange.Start, tiles[^1].LidRange.Start);
    }

    [Fact]
    public async Task FullTailShouldNotRequestAnything()
    {
        // arrange
        var tiles = NewTiles(rowsPerTile: 200, count: 3);
        var requested = new List<long>();

        // act
        var result = await ChatUI.AddPreviousRangeTiles([tiles[^1]], start => Fetch(tiles, requested, start));

        // assert
        requested.Should().BeEmpty();
        result.Should().Equal(tiles[^1]);
    }

    [Fact]
    public async Task WalkShouldStopAtTheFirstTile()
    {
        // arrange
        var tiles = NewTiles(rowsPerTile: 1, count: 3);
        var requested = new List<long>();

        // act
        var result = await ChatUI.AddPreviousRangeTiles([tiles[^1]], start => Fetch(tiles, requested, start));

        // assert
        result.Should().HaveCount(3);
        result[0].PreviousLidTileStart.Should().BeNull();
    }

    [Fact]
    public async Task WalkShouldBeBounded()
    {
        // arrange
        var tiles = NewTiles(rowsPerTile: 1, count: 20);
        var requested = new List<long>();

        // act
        await ChatUI.AddPreviousRangeTiles([tiles[^1]], start => Fetch(tiles, requested, start));

        // assert
        requested.Should().HaveCount(4);
    }

    private static ChatRangeTile[] NewTiles(int rowsPerTile, int count)
        => Enumerable.Range(0, count).Select(i => new ChatRangeTile(
            new(i * TileSize, (i + 1) * TileSize), [], [], rowsPerTile,
            i == 0 ? null : (i - 1) * TileSize,
            i == count - 1 ? null : (i + 1) * TileSize)).ToArray();

    private static Task<ChatRangeTile> Fetch(ChatRangeTile[] tiles, List<long> requested, long start)
    {
        requested.Add(start);
        return Task.FromResult(tiles.Single(m => m.LidRange.Start == start));
    }
}
