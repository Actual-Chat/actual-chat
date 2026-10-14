using ActualChat.Rpc;

namespace ActualChat.Api.Contracts.UnitTests.Rpc;

public class RpcPathPlannerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Candidates = ["main", "kz1"];

    [Fact]
    public void UnknownNetworkShouldStartOnTheOriginAndHedgeToTheRelay()
    {
        // arrange
        var history = new RpcPathHistory();

        // act
        var plan = RpcPathPlanner.Plan(Candidates, history, Now);

        // assert
        plan.HostsToTry.Should().Equal("main", "kz1");
        plan.StartNextAfter.Should().Be(RpcPathPlanner.DefaultStartNextAfter);
        plan.HostsToRefresh.Should().Equal("kz1");
    }

    [Fact]
    public void FreshPassedFirstChoiceShouldNeedNoHedgeAndNoVerification()
    {
        // arrange
        var history = new RpcPathHistory();
        history.Record("main", true, 80, Now.AddHours(-9));
        history.Record("main", true, 80, Now.AddHours(-1));

        // act
        var plan = RpcPathPlanner.Plan(Candidates, history, Now);

        // assert
        plan.FirstHost.Should().Be("main");
        plan.StartNextAfter.Should().BeNull();
        plan.HostsToRefresh.Should().BeEmpty();
    }

    [Fact]
    public void StalePassedFirstChoiceShouldVerifyTheOthers()
    {
        // arrange: the host passed, and it was a day and a half ago; the origin failed then
        var history = new RpcPathHistory();
        history.Record("main", false, 0, Now.AddHours(-37));
        history.Record("kz1", true, 400, Now.AddHours(-36));

        // act
        var plan = RpcPathPlanner.Plan(Candidates, history, Now);

        // assert
        plan.FirstHost.Should().Be("kz1");
        plan.HostsToRefresh.Should().Equal(["main"], "the origin may have recovered");
    }

    [Fact]
    public void ClearlyBetterRelayShouldLeadTheOrder()
    {
        // arrange
        var history = new RpcPathHistory();
        history.Record("main", false, 0, Now.AddHours(-5));
        history.Record("main", false, 0, Now.AddHours(-3));
        history.Record("main", false, 0, Now.AddMinutes(-20));
        history.Record("kz1", true, 400, Now.AddHours(-1));

        // act
        var plan = RpcPathPlanner.Plan(Candidates, history, Now);

        // assert
        plan.FirstHost.Should().Be("kz1");
        plan.StartNextAfter.Should().BeNull("the host passed within a day");
        plan.HostsToRefresh.Should().BeEmpty("the origin failed twenty minutes ago");
    }

    [Fact]
    public void KnownBadFirstChoiceShouldStartTheNextOneAtOnce()
    {
        // arrange: both are bad and the origin is not worse by a clear margin, so it stays first
        var history = new RpcPathHistory();
        history.Record("main", true, 80, Now.AddHours(-7));
        history.Record("main", false, 0, Now.AddHours(-5));
        history.Record("main", false, 0, Now.AddHours(-3));
        history.Record("main", false, 0, Now.AddHours(-1));
        history.Record("kz1", true, 400, Now.AddHours(-7));
        history.Record("kz1", false, 0, Now.AddHours(-3));

        // act
        var plan = RpcPathPlanner.Plan(Candidates, history, Now);

        // assert
        plan.FirstHost.Should().Be("main");
        plan.StartNextAfter.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void BadOriginAgainstUnknownRelayShouldLeadWithTheRelay()
    {
        // arrange
        var history = new RpcPathHistory();
        history.Record("main", false, 0, Now.AddHours(-3));
        history.Record("main", false, 0, Now.AddMinutes(-20));

        // act
        var plan = RpcPathPlanner.Plan(Candidates, history, Now);

        // assert: the host is unknown (neutral 0.5) and the origin scores 0
        plan.FirstHost.Should().Be("kz1");
        plan.StartNextAfter.Should().Be(RpcPathPlanner.DefaultStartNextAfter);
        plan.HostsToRefresh.Should().BeEmpty("the origin failed twenty minutes ago");
    }

    [Fact]
    public void SmallScoreDifferenceShouldKeepTheListOrder()
    {
        // arrange: the origin failed once, the host passed - but not by a clear margin
        var history = new RpcPathHistory();
        history.Record("main", true, 80, Now.AddHours(-9));
        history.Record("main", true, 80, Now.AddHours(-7));
        history.Record("main", false, 0, Now.AddHours(-5));
        history.Record("main", true, 80, Now.AddHours(-3));
        history.Record("main", true, 80, Now.AddHours(-1));
        history.Record("kz1", true, 400, Now.AddHours(-1));

        // act
        var plan = RpcPathPlanner.Plan(Candidates, history, Now);

        // assert
        plan.FirstHost.Should().Be("main");
    }

    [Fact]
    public void RecentlyFailedOtherShouldNotBeVerifiedAgainYet()
    {
        // arrange
        var history = new RpcPathHistory();
        history.Record("main", true, 80, Now.AddHours(-30));
        history.Record("kz1", false, 0, Now.AddMinutes(-10));

        // act
        var plan = RpcPathPlanner.Plan(Candidates, history, Now);

        // assert
        plan.FirstHost.Should().Be("main");
        plan.HostsToRefresh.Should().BeEmpty("the host failed ten minutes ago");
    }

    [Fact]
    public void SingleCandidateShouldNeverHedge()
    {
        // arrange
        var history = new RpcPathHistory();

        // act
        var plan = RpcPathPlanner.Plan(["main"], history, Now);

        // assert
        plan.FirstHost.Should().Be("main");
        plan.StartNextAfter.Should().BeNull();
        plan.HostsToRefresh.Should().BeEmpty();
    }

    [Fact]
    public void NoCandidatesShouldThrow()
    {
        // act
        var act = () => RpcPathPlanner.Plan([], new RpcPathHistory(), Now);

        // assert
        act.Should().Throw<ArgumentException>();
    }
}
