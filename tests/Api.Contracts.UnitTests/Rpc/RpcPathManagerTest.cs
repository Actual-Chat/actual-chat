using ActualChat.Rpc;
using ActualLab.Time.Testing;

namespace ActualChat.Api.Contracts.UnitTests.Rpc;

public class RpcPathManagerTest(ITestOutputHelper @out) : TestBase(@out), IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] Candidates = ["main", "kz1"];

    private readonly TestClock _clock = new TestClock().SetTo(Now.ToMoment());
    private readonly FakeStore _store = new();
    private readonly FakeNetworkInfoProvider _networkInfoProvider = new();
    private readonly FakeProbe _probe = new();

    [Fact]
    public async Task OriginThatPassesShouldStayAndTheRelayShouldBeChecked()
    {
        // arrange
        _probe.Pass("main");
        _probe.Pass("kz1", 400);
        await using var manager = Start();

        // act
        await TestWait.WhenPolled(() => {
            _store.GetHistory("net").GetChecks("kz1").Should().HaveCount(1);
            return Task.CompletedTask;
        });

        // assert
        manager.Current.Should().Be("main");
        _store.GetHistory("net").GetChecks("main").Single().IsOk.Should().BeTrue();
    }

    [Fact]
    public async Task FailedOriginShouldMoveToTheRelay()
    {
        // arrange
        _probe.Fail("main");
        _probe.Pass("kz1", 400);
        await using var manager = Start();

        // act
        await TestWait.WhenPolled(() => {
            manager.Current.Should().Be("kz1");
            return Task.CompletedTask;
        });

        // assert
        _store.GetHistory("net").GetChecks("main").Single().IsOk.Should().BeFalse();
    }

    [Fact]
    public async Task SlowOriginShouldBeHedgedToTheRelayAndTakenBackWhenItPasses()
    {
        // arrange
        _probe.Pass("kz1", 400);
        _clock.SpeedupBy(1000);
        await using var manager = Start();

        // act: the origin stays silent past the hedge delay, which passes at once on the fast clock
        await TestWait.WhenPolled(() => {
            manager.Current.Should().Be("kz1");
            return Task.CompletedTask;
        });
        _probe.Complete("main", new RpcPathMeasurement(TimeSpan.FromMilliseconds(90), TimeSpan.FromMilliseconds(300)));

        // assert
        await TestWait.WhenPolled(() => {
            manager.Current.Should().Be("main");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task FreshPassedFirstChoiceShouldNotBeChecked()
    {
        // arrange
        var history = new RpcPathHistory();
        history.Record("main", true, 80, Now.AddHours(-9));
        history.Record("main", true, 80, Now.AddHours(-1));
        _store.Seed("net", history);

        // act
        await using var manager = Start();
        await TestWait.WhenPolled(() => {
            manager.CycleCount.Should().BeGreaterThan(0);
            return Task.CompletedTask;
        });

        // assert
        manager.Current.Should().Be("main");
        _probe.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RecoveredOriginShouldBeTakenBackFromAStaleRelay()
    {
        // arrange: the host passed a day and a half ago, the origin failed before that
        var history = new RpcPathHistory();
        history.Record("main", false, 0, Now.AddHours(-37));
        history.Record("kz1", true, 400, Now.AddHours(-36));
        _store.Seed("net", history);
        _probe.Pass("main");
        _probe.Pass("kz1", 400);

        // act
        await using var manager = Start();

        // assert
        await TestWait.WhenPolled(() => {
            manager.CycleCount.Should().BeGreaterThan(0);
            return Task.CompletedTask;
        });
        manager.Current.Should().Be("main");
        _probe.Calls.Should().Contain(["kz1", "main"]);
    }

    [Fact]
    public async Task NetworkChangeShouldLoadThatNetworksHistoryAndVerifyEverything()
    {
        // arrange: the first network is known good, so nothing is checked on it
        var history = new RpcPathHistory();
        history.Record("main", true, 80, Now.AddHours(-9));
        history.Record("main", true, 80, Now.AddHours(-1));
        _store.Seed("net", history);
        _probe.Pass("main");
        _probe.Pass("kz1", 400);
        await using var manager = Start();
        await TestWait.WhenPolled(() => {
            manager.CycleCount.Should().Be(1);
            return Task.CompletedTask;
        });
        _probe.Calls.Should().BeEmpty();

        // act
        _networkInfoProvider.ChangeNetwork("other");

        // assert
        await TestWait.WhenPolled(() => {
            _store.GetHistory("other").GetChecks("kz1").Should().HaveCount(1);
            return Task.CompletedTask;
        });
        _store.GetHistory("other").GetChecks("main").Should().HaveCount(1);
    }

    [Fact]
    public async Task ReportedFailureShouldMoveToAnotherRelay()
    {
        // arrange
        var history = new RpcPathHistory();
        history.Record("main", true, 80, Now.AddHours(-9));
        history.Record("main", true, 80, Now.AddHours(-1));
        history.Record("kz1", true, 400, Now.AddHours(-1));
        _store.Seed("net", history);
        await using var manager = Start();
        await TestWait.WhenPolled(() => {
            manager.CycleCount.Should().Be(1);
            return Task.CompletedTask;
        });

        // act
        await manager.ReportFailure("main");

        // assert
        await TestWait.WhenPolled(() => {
            manager.Current.Should().Be("kz1");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ResultObtainedWhileOfflineShouldNotBeRecorded()
    {
        // arrange
        _probe.Pass("kz1", 400);
        await using var manager = Start();
        await TestWait.WhenPolled(() => {
            _probe.Calls.Should().Equal("main");
            return Task.CompletedTask;
        });

        // act: the network goes away, and the silent origin is reported as failed
        _networkInfoProvider.GoOffline();
        await TestWait.WhenPolled(() => {
            manager.IsReady.Should().BeFalse();
            return Task.CompletedTask;
        });
        _probe.Complete("main", null);
        await TestWait.WhenPolled(() => {
            manager.CycleCount.Should().BeGreaterThan(0);
            return Task.CompletedTask;
        });

        // assert
        _store.SaveCount.Should().Be(0);
        _store.GetHistory("net").GetChecks("main").Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAllShouldCheckEveryCandidateAndTellSoToWhoListens()
    {
        // arrange
        var history = new RpcPathHistory();
        history.Record("main", true, 80, Now.AddHours(-9));
        history.Record("main", true, 80, Now.AddHours(-1));
        _store.Seed("net", history);
        _probe.Pass("main", 120);
        _probe.Fail("kz1");
        await using var manager = Start();
        await TestWait.WhenPolled(() => {
            manager.CycleCount.Should().Be(1);
            return Task.CompletedTask;
        });
        var updateCount = 0;
        manager.Updated += () => Interlocked.Increment(ref updateCount);

        // act
        await manager.CheckAll(default);

        // assert
        var latest = manager.GetLatestChecks();
        latest[0].Should().Match<RpcPathCheck?>(x => x!.IsOk && x.PingTimeMs == 120);
        latest[1].Should().Match<RpcPathCheck?>(x => !x!.IsOk);
        updateCount.Should().Be(2);
    }

    [Fact]
    public async Task CheckAllShouldDoNothingWhileOffline()
    {
        // arrange
        var history = new RpcPathHistory();
        history.Record("main", true, 80, Now.AddHours(-9));
        history.Record("main", true, 80, Now.AddHours(-1));
        _store.Seed("net", history);
        await using var manager = Start();
        await TestWait.WhenPolled(() => {
            manager.CycleCount.Should().Be(1);
            return Task.CompletedTask;
        });
        _networkInfoProvider.GoOffline();
        await TestWait.WhenPolled(() => {
            manager.IsReady.Should().BeFalse();
            return Task.CompletedTask;
        });

        // act
        await manager.CheckAll(default);

        // assert
        _probe.Calls.Should().BeEmpty();
    }

    public void Dispose()
        => _clock.Dispose();

    [Fact]
    public async Task AttestationShouldBeFetchedBeforeSwitchingToARelay()
    {
        // arrange
        var events = new List<string>();
        _probe.Fail("main");
        _probe.Pass("kz1", 400);
        _probe.SetAttestation("kz1", "attestation of kz1");
        await using var manager = New();
        manager.AttestationUpdated += (host, _) => { lock (events) events.Add("attestation " + host); };
        manager.CurrentChanged += host => { lock (events) events.Add("current " + host); };

        // act
        manager.Start();

        // assert: Current is set before CurrentChanged fires, so the wait is on the event
        await TestWait.WhenPolled(() => {
            lock (events)
                events.Should().Contain("current kz1");
            return Task.CompletedTask;
        });
        manager.Current.Should().Be("kz1");
        lock (events)
            events.Should().Equal("attestation kz1", "current kz1");
    }

    [Fact]
    public async Task OriginShouldNeverAskForAnAttestation()
    {
        // arrange
        _probe.Pass("main");
        _probe.Pass("kz1", 400);
        var attestations = new List<string>();
        await using var manager = New();
        manager.AttestationUpdated += (host, _) => { lock (attestations) attestations.Add(host); };

        // act
        manager.Start();
        await TestWait.WhenPolled(() => {
            manager.CycleCount.Should().BeGreaterThan(0);
            return Task.CompletedTask;
        });

        // assert
        manager.Current.Should().Be("main");
        lock (attestations)
            attestations.Should().BeEmpty();
    }

    [Fact]
    public async Task SilentRelayShouldNotBlockTheSwitch()
    {
        // arrange
        _probe.Fail("main");
        _probe.Pass("kz1", 400);
        _probe.SetAttestation("kz1", null);
        await using var manager = Start();

        // act & assert
        await TestWait.WhenPolled(() => {
            manager.Current.Should().Be("kz1");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RestoredRelayShouldBeLeftForTheOriginThePlanPicks()
    {
        // arrange: the app restored the relay from its last run, and the origin is known good
        var history = new RpcPathHistory();
        history.Record("main", true, 80, Now.AddHours(-9));
        history.Record("main", true, 80, Now.AddHours(-1));
        _store.Seed("net", history);
        var switches = new List<string>();
        await using var manager = New("kz1");
        manager.CurrentChanged += host => { lock (switches) switches.Add(host); };

        // act
        manager.Start();
        await TestWait.WhenPolled(() => {
            manager.CycleCount.Should().BeGreaterThan(0);
            return Task.CompletedTask;
        });

        // assert
        manager.Current.Should().Be("main");
        lock (switches)
            switches.Should().Equal(["main"], "whoever holds the restored relay must be told to leave it");
    }

    // Private methods

    private RpcPathManager Start(string? current = null)
    {
        var manager = New(current);
        manager.Start();
        return manager;
    }

    private RpcPathManager New(string? current = null)
        => new(Candidates, current ?? Candidates[0], _store, _networkInfoProvider, _probe, _clock);
}
