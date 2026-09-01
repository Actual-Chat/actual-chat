namespace ActualChat.Core.UnitTests.Mathematics;

public class TileLayerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly TileLayer<long> Tiles16 = new(0L, 16L);
    private static readonly TileLayer<long> Tiles64 = new(0L, 64L);

    [Fact]
    public void GetTileTest()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tiles16.GetTile((0, 17)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Tiles16.GetTile((1, 17)));
        Tiles16.GetTile((16, 32)).Should().Be(new Tile<long>(16, 32, Tiles16));
        Tiles16.IsTile((0, 16)).Should().BeTrue();
        Tiles16.IsTile((1, 17)).Should().BeFalse();

        Tiles16.GetTile(1).Start.Should().Be(0);
        Tiles16.GetTile(17).Start.Should().Be(16);
        Tiles64.GetTile(257).Start.Should().Be(256);

        var tile = Tiles16.GetTile(-16);
        tile.Should().Be(new Tile<long>(-16, 0, Tiles16));
        tile.Next().Should().Be(new Tile<long>(0, 16, Tiles16));
        tile.Next(2).Should().Be(new Tile<long>(16, 32, Tiles16));
        tile.Prev().Should().Be(new Tile<long>(-32, -16, Tiles16));
        tile.Prev(2).Should().Be(new Tile<long>(-48, -32, Tiles16));
    }

    [Fact]
    public void NonZeroOffsetTest()
    {
        var tiles = new TileLayer<long>(3L, 10L);
        tiles.GetTile(3).Range.Should().Be(new Range<long>(3, 13));
        tiles.GetTile(2).Range.Should().Be(new Range<long>(-7, 3));
        tiles.IsTile((13, 23)).Should().BeTrue();
        tiles.IsTile((10, 20)).Should().BeFalse();
    }

    [Fact]
    public void GetCoveringTilesTest()
    {
        Tiles16.GetCoveringTiles((0, 0)).Should().BeEmpty();
        Tiles16.GetCoveringTiles((0, -1)).Should().BeEmpty();
        Tiles16.GetCoveringTiles((-16, -16)).Should().BeEmpty();
        Tiles16.GetCoveringTiles((-16, -17)).Should().BeEmpty();

        Tiles16.GetCoveringTiles((-1, 1)).Select(t => t.Range)
            .Should()
            .BeEquivalentTo(new Range<long>[] {
                (-16, 0),
                (0, 16),
            });
        Tiles64.GetCoveringTiles((-65, 1)).Select(t => t.Range)
            .Should()
            .BeEquivalentTo(new Range<long>[] {
                (-128, -64),
                (-64, 0),
                (0, 64),
            });
    }

    [Fact]
    public void RandomTileCoverTest()
    {
        var rnd = new Random(12);
        for (var i = 0; i < 10_000; i++) {
            var range = new Range<long>(rnd.Next(20_000), rnd.Next(20_000)).Normalize();
            var tiles = Tiles16.GetCoveringTiles(range);
            if (range.IsEmptyOrNegative) {
                tiles.Should().BeEmpty();
                continue;
            }

            var union = tiles[0].Range;
            foreach (var tile in tiles.Skip(1)) {
                union.End.Should().Be(tile.Start);
                union = (union.Start, tile.End);
            }

            var startGap = range.Start - union.Start;
            startGap.Should().BeGreaterThanOrEqualTo(0);
            startGap.Should().BeLessThan(Tiles16.TileSize);

            var endGap = union.End - range.End;
            endGap.Should().BeGreaterThanOrEqualTo(0);
            endGap.Should().BeLessThan(Tiles16.TileSize);
        }
    }
}
