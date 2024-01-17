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
        await using (var h = await locks.Acquire(key, "", lockOptions)) {
            for (var i = 0; i < 10; i++) { // 2s
                await Task.Delay(TimeSpan.FromSeconds(0.2));
                var info = await locks.TryQuery(key);
                info!.HolderId.Should().Be(h.Id);
            }
        }
        {
            var info = await locks.TryQuery(key);
            info.Should().BeNull();
        }
    }

    [Fact]
    public async Task LockIsGoneTest()
    {
        using var appHost = await NewAppHost();
        var locks = appHost.Services.GetRequiredService<IMeshLocks<InfrastructureDbContext>>();
        var lockOptions = locks.LockOptions with { ExpirationPeriod = TimeSpan.FromSeconds(1.5) };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        await using var h = await locks.Acquire(key, "", lockOptions);

        await locks.Backend.ForceRelease(key, false);
        (await locks.TryQuery(key)).Should().BeNull();

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
        await using var h1 = await locks.Acquire(key, "", lockOptions);

        var h2AcquireTask = locks.Acquire(key, "", lockOptions);
        await Task.Delay(TimeSpan.FromSeconds(0.5)); // WhenChanged needs some time to subscribe
        h2AcquireTask.IsCompleted.Should().BeFalse();

        await h1.DisposeAsync();
        var startedAt = CpuTimestamp.Now;
        await using var h2 = await h2AcquireTask;
        startedAt.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
}
