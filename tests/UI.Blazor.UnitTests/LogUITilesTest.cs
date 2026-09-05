using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.UnitTests;

public sealed class LogUITilesTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly TileLayer<long> IdTiles = Constants.TileLayers.Long5;

    [Fact]
    public void GetEntriesShouldCoverEveryTileAfterBufferWraps()
    {
        // arrange - an 8-slot ring that has wrapped: ids 7..13 are live, split as [7, 8] + [9..13]
        var events = new RingBuffer<LogEntry>(7);
        for (var id = 1L; id <= 13; id++)
            events.PushTailAndMoveHeadIfFull(NewEntry(id));
        var idRange = new Range<long>(events[0].Id, events[^1].Id + 1);
        var idTiles = IdTiles.GetCoveringTiles(idRange).OrderBy(x => x.Start).ToList();

        // act
        var entries = idTiles.SelectMany(idTile => LogUI.GetEntries(events, idTile.Range)).ToList();

        // assert
        idTiles.Should().HaveCount(2, "ids 7..13 span two 5-wide tiles");
        entries.Select(x => x.Id).Should().Equal(7, 8, 9, 10, 11, 12, 13);
    }

    [Fact]
    public void GetEntriesShouldReturnNothingForTileOutsideBuffer()
    {
        // arrange
        var events = new RingBuffer<LogEntry>(7);
        for (var id = 1L; id <= 13; id++)
            events.PushTailAndMoveHeadIfFull(NewEntry(id));

        // act
        var before = LogUI.GetEntries(events, new Range<long>(0, 5));
        var after = LogUI.GetEntries(events, new Range<long>(15, 20));

        // assert
        before.Should().BeEmpty("ids 0..4 were already evicted");
        after.Should().BeEmpty("ids 15..19 don't exist yet");
    }

    // Private methods

    private static LogEntry NewEntry(long id)
        => new(id, "Test", LogLevel.Information, default,
            Message: $"#{id}", Exception: null, Timestamp: default);
}
