using System.Diagnostics;

namespace ActualChat.Users.UnitTests;

public class SortedCheckInsTest
{
    private readonly SortedCheckIns _sut;
    private TestData Data { get; } = new ();

    public SortedCheckInsTest()
    {
        _sut = new SortedCheckIns();
    }

    [Fact]
    public void ShouldPopEmpty()
    {
        // arrange
        var now = Data.Now;

        // act
        var checkIns = _sut.PopRange(now);

        // assert
        checkIns.Should().BeEmpty();
    }

    [Fact]
    public void PrevShouldBeNullWhenUserIsNotInList()
    {
        // arrange
        // act
        var prev = _sut.Set(Data.User1Now);
        var prev2 = _sut.Set(Data.User2Now);

        // assert
        prev.Should().BeNull();
        prev2.Should().BeNull();
    }

    [Fact]
    public void PrevShouldNotBeNullWhenUserIsInList()
    {
        // arrange
        var checkIn1 = Data.User1At(-10);
        var checkIn2 = Data.User1Now;

        // act
        var prev = _sut.Set(checkIn1);
        var prev2 = _sut.Set(checkIn2);

        // assert
        prev.Should().BeNull();
        prev2.Should().BeEquivalentTo(checkIn1);
    }

    [Fact]
    public void ShouldPopOnlyOnce()
    {
        // arrange
        // act
        _sut.Set(Data.User1Now);
        _sut.Set(Data.User2Now);

        var popped = _sut.PopRange(Data.Now);
        var popped2 = _sut.PopRange(Data.Now);

        // assert
        popped.Should().BeEquivalentTo(new[] { Data.User1Now, Data.User2Now }, o => o.WithStrictOrdering());
        popped2.Should().BeEmpty();
    }

    [Fact]
    public void ShouldReplaceSameUser()
    {
        // arrange
        Moment now = Data.Now;

        // act
        _sut.Set(Data.User1At(-20));
        _sut.Set(Data.User1At(-10));
        _sut.Set(Data.User1Now);

        var popped = _sut.PopRange(now);

        // assert
        popped.Should().BeEquivalentTo(new[] { Data.User1Now });
    }

    [Fact]
    public void ShouldSortLargeSetAndPopAllAtOnce()
    {
        // arrange
        const int userCount = 100_000;
        const int maxMomentCount = 1_000;

        // act
        for (int i = 0; i < 10 * userCount; i++) {
            var userCheckIn = Data.RandomCheckIn(userCount, maxMomentCount);
            _sut.Set(userCheckIn);
        }

        var popped = _sut.PopRange(Data.Now);

        // assert
        popped.Should().BeInAscendingOrder(x => x.At);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, 3, 3)]
    [InlineData(1, 10, 1)]
    [InlineData(1, 10, 3)]
    [InlineData(1, 10, 10)]
    [InlineData(10, 1, 1)]
    [InlineData(10, 10, 7)]
    [InlineData(10, 10, 10)]
    [InlineData(10, 100, 1)]
    [InlineData(10, 100, 27)]
    [InlineData(10, 100, 100)]
    [InlineData(100, 10, 1)]
    [InlineData(100, 10, 5)]
    [InlineData(100, 10, 10)]
    [InlineData(100, 100, 1)]
    [InlineData(100, 100, 2)]
    [InlineData(100, 100, 99)]
    [InlineData(100, 100, 100)]
    [InlineData(10, 10_000, 1)]
    [InlineData(10, 10_000, 100)]
    [InlineData(10, 10_000, 1000)]
    public void ShouldSortAndPopInPortions(int iterationCount, int portionSize, int popIntervalCount)
    {
        // arrange
        const int maxUserCount = 100_000;
        const int maxMomentCount = 1_000;

        // act
        for (int iteration = 0; iteration < iterationCount; iteration++) {
            var checkIns = Enumerable.Repeat(0, portionSize)
                .Select(_ => Data.RandomCheckIn(maxUserCount, maxMomentCount))
                .ToList();
            foreach (var checkIn in checkIns)
                _sut.Set(checkIn);

            var remainingExpected = checkIns.OrderByDescending(x => x.At)
                .DistinctBy(x => x.UserId)
                .OrderBy(x => x.At)
                .ThenBy(x => x.UserId)
                .ToList();

            var popStepSec = maxMomentCount / popIntervalCount;

            for (int i = popIntervalCount - 1; i >= 0; i--) {
                var shiftSec = i * popStepSec;
                var maxAt = Data.Now - TimeSpan.FromSeconds(shiftSec);
                var popped = _sut.PopRange(maxAt);

                // assert
                var userDuplicates = popped.GroupBy(x => x.UserId).Where(x => x.Skip(1).Any());
                userDuplicates.Should().BeEmpty();

                var expected = remainingExpected.TakeWhile(x => x.At <= maxAt).ToList();
                popped.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering());
                remainingExpected = remainingExpected.Skip(expected.Count).ToList();
            }

            var leftItems = _sut.PopRange(Moment.MaxValue);
            leftItems.Should().BeEmpty();
        }
    }

    [Fact(Timeout = 20_000)]
    public async Task ShouldRunConcurrentSafe()
    {
        // arrange
        const int userCount = 100_000;
        const int maxMomentCount = 1_000;
        const int checkInCount = 10 * userCount;
        var stop = new CancellationTokenSource();

        // act
        await Task.WhenAll(BackgroundTask.Run(SetLoop), BackgroundTask.Run(PopLoop));

        Task SetLoop()
        {
            // act
            for (int i = 0; i < checkInCount; i++)
                _sut.Set(Data.RandomCheckIn(userCount, maxMomentCount));
            stop.Cancel();

            return Task.CompletedTask;
        }

        Task PopLoop()
        {
            while (!stop.IsCancellationRequested)
                PopOnce();
            PopOnce();

            return Task.CompletedTask;
        }

        void PopOnce()
        {
            // act
            var popped = _sut.PopRange(Data.Now);

            // assert
            popped.Should().BeInAscendingOrder(x => x.At);
        }
    }

    private class TestData
    {
        private readonly Random _userIdRng = new (123);
        private readonly Random _timeShiftRng = new (456);
        private UserId User1 { get; } = new ("User111");
        private UserId User2 { get; } = new ("User222");
        public Moment Now { get; } = Moment.Parse("2023-04-10T09:37:05.6506313Z"); // intentionally fixed for tests
        public UserCheckIn User1Now => new (User1, Now);
        public UserCheckIn User2Now => new (User2, Now);

        public UserCheckIn User1At(int shiftSeconds)
            => new (User1, Now + TimeSpan.FromSeconds(shiftSeconds));

        public UserCheckIn RandomCheckIn(int maxUserCount, int maxShiftSeconds)
        {
            var userIdFormat = new string('0', (int)Math.Log10(maxUserCount));
            var at = Now - TimeSpan.FromSeconds(_timeShiftRng.Next(maxShiftSeconds));
            var userSid = _userIdRng.Next(maxUserCount).ToString(userIdFormat);
            var userId = new UserId(userSid);
            return new UserCheckIn(userId, at);
        }
    }
}
