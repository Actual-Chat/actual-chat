using ActualChat.Mathematics;

namespace ActualChat.Core.UnitTests
{
    public class LogCoverTest : TestBase
    {
        public LogCoverTest(ITestOutputHelper @out) : base(@out) { }

        [Fact]
        public void MomentLogCoverTest()
        {
            var c = LogCover.Default.Moment;
            c.MinTileSize.Should().Be(TimeSpan.FromMinutes(3));
            c.TileSizeFactor.Should().Be(4);
            c.TileSizes.First().Should().Be(c.MinTileSize);
            c.TileSizes.Last().Should().Be(c.MaxTileSize);
            (c.TileSizes[1] / c.MinTileSize).Should().Be(c.TileSizeFactor);
            c.TileSizes.Length.Should().Be(11);

            c.GetTileStart(c.Zero - TimeSpan.FromMinutes(1), 0)
                .Should().Be(c.Zero - TimeSpan.FromMinutes(3));

            c.GetTileStart(c.Zero + TimeSpan.FromMinutes(1), 0)
                .Should().Be(c.Zero);
            c.GetTileStart(c.Zero + TimeSpan.FromMinutes(4), 0)
                .Should().Be(c.Zero + TimeSpan.FromMinutes(3));
            c.GetTileStart(c.Zero + TimeSpan.FromMinutes(4), 1)
                .Should().Be(c.Zero + TimeSpan.FromMinutes(0));
            c.GetTileStart(c.Zero + TimeSpan.FromMinutes(25), 1)
                .Should().Be(c.Zero + TimeSpan.FromMinutes(24));

            c.GetMinCoveringTile((Range<Moment>) (c.Zero - TimeSpan.FromMinutes(1), c.Zero))
                .Should().Be((Range<Moment>) (c.Zero - TimeSpan.FromMinutes(3), c.Zero));
        }

        [Fact]
        public void LongLogCoverTest()
        {
            var c = LogCover.Default.Long;
            c.MinTileSize.Should().Be(16);
            c.TileSizeFactor.Should().Be(4);
            c.TileSizes.First().Should().Be(c.MinTileSize);
            c.TileSizes.Last().Should().Be(c.MaxTileSize);
            (c.TileSizes[1] / c.MinTileSize).Should().Be(c.TileSizeFactor);
            c.TileSizes.Length.Should().Be(6);

            c.GetTileStart(-16, 0)
                .Should().Be(-16);

            c.GetTileStart(1, 0)
                .Should().Be(0);
            c.GetTileStart(17, 0)
                .Should().Be(16);
            c.GetTileStart(16, 1)
                .Should().Be(0);
            c.GetTileStart(257, 1)
                .Should().Be(256);

            c.GetMinCoveringTile((-16, 0))
                .Should().Be((Range<long>) (-16, 0));
            c.GetMinCoveringTile((-17, 0))
                .Should().Be((Range<long>) (-64, 0));

            c.GetTileCover((-17, 257))
                .Should().BeEquivalentTo(new Range<long>[] {
                    (-32, -16),
                    (-16, 0),
                    (0, 256),
                    (256, 256 + 16),
                });

            c.GetTileCover((-65, 257))
                .Should().BeEquivalentTo(new Range<long>[] {
                    (-80, -64),
                    (-64, 0),
                    (0, 256),
                    (256, 256 + 16),
                });
        }
    }
}
