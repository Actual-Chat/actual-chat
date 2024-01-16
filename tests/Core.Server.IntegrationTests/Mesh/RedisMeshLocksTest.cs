using ActualChat.Mesh;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Mesh;

public class RedisMeshLocksTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    [Fact]
    public async Task BasicTest()
    {
        using var appHost = await NewAppHost();
        var locks = appHost.Services.GetRequiredService<IMeshLocks<InfrastructureDbContext>>();
        var lockOptions = locks.LockOptions with { ExpirationPeriod = TimeSpan.FromSeconds(1) };

        await using var h = await locks.Acquire("x", "", lockOptions);
        await Task.Delay(TimeSpan.FromSeconds(0.25)); // Otherwise it fails on GitHub
        for (var i = 0; i < 10; i++) {
            var info = await locks.TryQuery(h.Key);
            info!.HolderId.Should().Be(h.Id);
            await Task.Delay(TimeSpan.FromSeconds(0.333));
        }
        await h.DisposeAsync();
        {
            await Task.Delay(TimeSpan.FromSeconds(0.25)); // Otherwise it fails on GitHub
            var info = await locks.TryQuery(h.Key);
            info.Should().BeNull();
        }
    }

    [Fact]
    public async Task LockIsGoneTest()
    {
        using var appHost = await NewAppHost();
        var locks = appHost.Services.GetRequiredService<IMeshLocks<InfrastructureDbContext>>();
        var lockOptions = locks.LockOptions with { ExpirationPeriod = TimeSpan.FromSeconds(1) };

        await using var h = await locks.Acquire("x", "", lockOptions);
        await locks.Backend.ForceRelease(h.Key, false);
        (await locks.TryQuery(h.Key)).Should().BeNull();

        await Task.Delay(TimeSpan.FromSeconds(1.25));
        h.StopToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseNotifyTest()
    {
        using var appHost = await NewAppHost();
        var locks = appHost.Services.GetRequiredService<IMeshLocks<InfrastructureDbContext>>();
        var lockOptions = locks.LockOptions with { ExpirationPeriod = TimeSpan.FromSeconds(10) };

        await using var h1 = await locks.Acquire("x", "", lockOptions);
        var h2AcquireTask = locks.Acquire("x", "", lockOptions);
        await Task.Delay(TimeSpan.FromSeconds(0.5)); // WhenChanged needs some time to subscribe
        h2AcquireTask.IsCompleted.Should().BeFalse();

        await h1.DisposeAsync();
        var startedAt = CpuTimestamp.Now;
        await using var h2 = await h2AcquireTask;
        startedAt.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
}
