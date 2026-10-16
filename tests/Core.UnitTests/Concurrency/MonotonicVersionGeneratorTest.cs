using ActualChat.Concurrency;
using ActualLab.Time.Testing;
using ActualLab.Versioning;

namespace ActualChat.Core.UnitTests.Concurrency;

public class MonotonicVersionGeneratorTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task ConcurrentCallsWithinOneClockTickShouldGetUniqueVersions()
    {
        // arrange
        using var frozenClock = new TestClock(TimeSpan.FromDays(20_000), default, 0);
        var clockBased = new ClockBasedVersionGenerator(frozenClock);
        var sut = new MonotonicVersionGenerator(clockBased);

        // act
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => Enumerable.Range(0, 1000).Select(_ => sut.NextVersion()).ToList()))
            .ToList();
        var batches = await Task.WhenAll(tasks);
        var versions = batches.SelectMany(x => x).ToList();

        // assert
        clockBased.NextVersion().Should().Be(clockBased.NextVersion(), "the wrapped clock doesn't move");
        versions.Should().HaveCount(8000).And.OnlyHaveUniqueItems();
        versions.Min().Should().BeGreaterThan(frozenClock.Now.EpochOffset.Ticks - 1);
    }

    [Fact]
    public void NextVersionShouldExceedTheCurrentOne()
    {
        // arrange
        using var frozenClock = new TestClock(TimeSpan.FromDays(20_000), default, 0);
        var sut = new MonotonicVersionGenerator(new ClockBasedVersionGenerator(frozenClock));
        var future = frozenClock.Now.EpochOffset.Ticks + 1000;

        // act
        var version = sut.NextVersion(future);
        var next = sut.NextVersion();

        // assert
        version.Should().Be(future + 1);
        next.Should().Be(future + 2, "versions never go back below one already handed out");
    }
}
