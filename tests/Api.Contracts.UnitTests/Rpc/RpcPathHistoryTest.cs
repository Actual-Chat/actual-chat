using ActualChat.Rpc;

namespace ActualChat.Api.Contracts.UnitTests.Rpc;

public class RpcPathHistoryTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RelayWithoutChecksShouldScoreNeutral()
    {
        // arrange
        var history = new RpcPathHistory();

        // act
        var score = history.GetScore("kz1", Now);

        // assert
        score.Should().Be(RpcPathHistory.NeutralScore);
    }

    [Fact]
    public void ChecksInsideOneWindowShouldKeepOnlyTheNewest()
    {
        // arrange
        var history = new RpcPathHistory();

        // act
        history.Record("main", false, 0, Now);
        history.Record("main", true, 80, Now.AddMinutes(90));

        // assert
        var checks = history.GetChecks("main");
        checks.Should().HaveCount(1);
        checks[0].IsOk.Should().BeTrue("the newest result of a window wins");
        checks[0].PingTimeMs.Should().Be(80);
    }

    [Fact]
    public void ChecksInDifferentWindowsShouldAllBeKept()
    {
        // arrange
        var history = new RpcPathHistory();

        // act
        history.Record("main", true, 80, Now);
        history.Record("main", false, 0, Now.AddHours(2));

        // assert
        history.GetChecks("main").Select(x => x.IsOk).Should().Equal(true, false);
    }

    [Fact]
    public void OnlyTheNewestChecksShouldBeKept()
    {
        // arrange
        var history = new RpcPathHistory();

        // act
        for (var i = 0; i < RpcPathHistory.MaxChecks + 8; i++)
            history.Record("main", true, i, Now.AddHours(3 * i));

        // assert
        var checks = history.GetChecks("main");
        checks.Should().HaveCount(RpcPathHistory.MaxChecks);
        checks[0].PingTimeMs.Should().Be(8);
        checks[^1].PingTimeMs.Should().Be(RpcPathHistory.MaxChecks + 7);
    }

    [Fact]
    public void ScoreShouldCountRecentChecksMore()
    {
        // arrange: passed 20h ago, failed 5h ago, passed 1h ago
        var history = new RpcPathHistory();
        history.Record("kz1", true, 100, Now.AddHours(-20));
        history.Record("kz1", false, 0, Now.AddHours(-5));
        history.Record("kz1", true, 100, Now.AddHours(-1));

        // act
        var score = history.GetScore("kz1", Now);

        // assert
        score.Should().BeApproximately(0.64, 0.01);
    }

    [Fact]
    public void RecentFailureShouldOutweighOlderSuccesses()
    {
        // arrange: passed 20h and 5h ago, failed 1h ago
        var history = new RpcPathHistory();
        history.Record("kz1", true, 100, Now.AddHours(-20));
        history.Record("kz1", true, 100, Now.AddHours(-5));
        history.Record("kz1", false, 0, Now.AddHours(-1));

        // act
        var score = history.GetScore("kz1", Now);

        // assert
        score.Should().BeApproximately(0.43, 0.01);
    }

    [Fact]
    public void ScoreShouldNotMixRelays()
    {
        // arrange
        var history = new RpcPathHistory();
        history.Record("main", false, 0, Now.AddHours(-1));
        history.Record("kz1", true, 100, Now.AddHours(-1));

        // act & assert
        history.GetScore("main", Now).Should().Be(0);
        history.GetScore("kz1", Now).Should().Be(1);
    }

    [Fact]
    public void ToBytesShouldRoundTrip()
    {
        // arrange
        var history = new RpcPathHistory();
        history.Record("main", false, 0, Now.AddHours(-3));
        history.Record("main", true, 80, Now.AddHours(-1));
        history.Record("kz1", true, 400, Now.AddHours(-1));

        // act
        var restored = RpcPathHistory.FromBytesOrEmpty(history.ToBytes());

        // assert
        restored.GetChecks("main").Should().Equal(history.GetChecks("main"));
        restored.GetChecks("kz1").Should().Equal(history.GetChecks("kz1"));
    }

    [Fact]
    public void UnreadableBytesShouldGiveEmptyHistory()
    {
        // arrange
        byte[] garbage = [1, 2, 3, 4, 5, 250, 251];

        // act
        var history = RpcPathHistory.FromBytesOrEmpty(garbage);

        // assert
        history.GetChecks("main").Should().BeEmpty();
        RpcPathHistory.FromBytesOrEmpty(ReadOnlyMemory<byte>.Empty).GetChecks("main").Should().BeEmpty();
    }
}
