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
        var lockOptions = locks.LockOptions with { ExpirationPeriod = TimeSpan.FromSeconds(1.5) };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        var info = await locks.GetInfo(key);
        info.Should().BeNull();

        await using (var h = await locks.Lock(key, "", lockOptions)) {
            (await locks.TryLock(key, "")).Should().BeNull();
            for (var i = 0; i < 10; i++) { // 2s
                await Task.Delay(TimeSpan.FromSeconds(0.2));
                info = await locks.GetInfo(key);
                info!.HolderId.Should().Be(h.Id);
            }
        }

        info = await locks.GetInfo(key);
        info.Should().BeNull();
    }

    [Fact]
    public async Task LockIsGoneTest()
    {
        using var appHost = await NewAppHost();
        var locks = appHost.Services.GetRequiredService<IMeshLocks<InfrastructureDbContext>>();
        var lockOptions = locks.LockOptions with { ExpirationPeriod = TimeSpan.FromSeconds(1.5) };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        (await locks.GetInfo(key)).Should().BeNull();
        await using var h = await locks.Lock(key, "", lockOptions);
        (await locks.TryLock(key, "")).Should().BeNull();

        await locks.Backend.ForceRelease(key, false);
        (await locks.GetInfo(key)).Should().BeNull();

        await Task.Delay(TimeSpan.FromSeconds(1.5));
        h.StopToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseNotifyTest()
    {
        using var appHost = await NewAppHost();
        var locks = appHost.Services.GetRequiredService<IMeshLocks<InfrastructureDbContext>>();
        var lockOptions = locks.LockOptions with { ExpirationPeriod = TimeSpan.FromSeconds(10) };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        (await locks.GetInfo(key)).Should().BeNull();
        await using var h1 = await locks.Lock(key, "", lockOptions);
        (await locks.TryLock(key, "")).Should().BeNull();

        var h2AcquireTask = locks.Lock(key, "", lockOptions);
        await Task.Delay(TimeSpan.FromSeconds(0.5)); // WhenChanged needs some time to subscribe
        h2AcquireTask.IsCompleted.Should().BeFalse();

        await h1.DisposeAsync();
        var startedAt = CpuTimestamp.Now;
        await using var h2 = await h2AcquireTask;
        startedAt.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
}
