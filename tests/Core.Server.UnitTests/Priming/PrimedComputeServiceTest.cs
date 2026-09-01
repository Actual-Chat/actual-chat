namespace ActualChat.Core.Server.UnitTests.Priming;

public class PrimedComputeServiceTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task SetRoutesValueThroughPrimer()
    {
        var services = CreateServices();
        var svc = services.GetRequiredService<PrimedComputeService>();

        (await svc.Get("a")).Should().Be(0);
        svc.StorageReadCount.Should().Be(1);

        await svc.Set("a", 5);
        (await svc.Get("a")).Should().Be(5);
        svc.StorageReadCount.Should().Be(1); // primer hit, no extra storage read
        svc.Primer.GetReservationCount().Should().Be(0);   // primer entry consumed

        await svc.SetRaw("a", 7);
        (await svc.Get("a")).Should().Be(7);
        svc.StorageReadCount.Should().Be(2); // SetRaw bypasses primer
    }

    [Fact]
    public async Task ConcurrentSetsSerialize()
    {
        var services = CreateServices();
        var svc = services.GetRequiredService<PrimedComputeService>();

        var tasks = Enumerable.Range(1, 20)
            .Select(i => svc.Set("k", i))
            .ToArray();
        await Task.WhenAll(tasks);

        var final = await svc.Get("k");
        final.Should().BeInRange(1, 20);
        svc.Primer.GetReservationCount().Should().Be(0);
    }

    [Fact]
    public async Task TryLockAndPrepareSkipsSupersededComputed()
    {
        var services = CreateServices();
        var svc = services.GetRequiredService<PrimedComputeService>();
        var computed = await Computed.Capture(() => svc.Get("a", default));

        using (var primer = await svc.Primer.TryLockAndPrepare("a", computed))
            primer.Should().NotBeNull();

        computed.Invalidate();
        (await svc.Primer.TryLockAndPrepare("a", computed)).Should().BeNull();
        svc.Primer.GetReservationCount().Should().Be(0);
    }

    [Fact]
    public async Task TryLockAndPrepareRechecksAfterTheLock()
    {
        var services = CreateServices();
        var svc = services.GetRequiredService<PrimedComputeService>();
        var computed = await Computed.Capture(() => svc.Get("a", default));

        var blocker = await svc.Primer.LockAndPrepare("a");
        var pendingTask = svc.Primer.TryLockAndPrepare("a", computed).AsTask();
        await Task.Delay(50);
        pendingTask.IsCompleted.Should().BeFalse();

        // A writer supersedes the value while we're still queued for the lock
        computed.Invalidate();
        blocker.Dispose();

        (await pendingTask.WaitAsync(TimeSpan.FromSeconds(1))).Should().BeNull();
        svc.Primer.GetReservationCount().Should().Be(0);
    }

    private IServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddXUnit(Out));
        services.AddFusion().AddService<PrimedComputeService>();
        return services.BuildServiceProvider();
    }
}
