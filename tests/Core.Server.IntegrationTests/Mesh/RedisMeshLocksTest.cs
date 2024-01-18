using ActualChat.App.Server;
using ActualChat.Mesh;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Mesh;

public class RedisMeshLocksTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    private AppHost _appHost = null!;
    private IServiceProvider _services = null!;

    public override async Task InitializeAsync()
    {
        _appHost = await NewAppHost(TestAppHostOptions.None);
        _services = _appHost.Services;
    }

    public override Task DisposeAsync()
    {
        _appHost.Dispose();
        return Task.CompletedTask;
    }

    [Fact(Timeout = 30_000)]
    public async Task BasicTest()
    {
        var locks = _services.GetRequiredService<IMeshLocks<InfrastructureDbContext>>();
        var lockOptions = locks.LockOptions with {
            ExpirationPeriod = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 5 : 2),
        };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        var info = await locks.GetInfo(key);
        info.Should().BeNull();

        await using (var h = await locks.Lock(key, "", lockOptions)) {
            (await locks.TryLock(key, "")).Should().BeNull();
            var now = CpuTimestamp.Now;
            while (now.Elapsed <= lockOptions.ExpirationPeriod + TimeSpan.FromSeconds(0.5)) {
                await Task.Delay(TimeSpan.FromSeconds(0.5));
                info = await locks.GetInfo(key);
                if (info == null)
                    Assert.Fail($"info == null (elapsed = {now.Elapsed})");
                info.HolderId.Should().Be(h.Id);
            }
        }

        info = await locks.GetInfo(key);
        info.Should().BeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task LockIsGoneTest()
    {
        var locks = _services.GetRequiredService<IMeshLocks<InfrastructureDbContext>>();
        var lockOptions = locks.LockOptions with {
            ExpirationPeriod = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 5 : 2),
        };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        await using var changes = await locks.Changes("");

        (await locks.GetInfo(key)).Should().BeNull();
        await using var h = await locks.Lock(key, "", lockOptions);
        (await locks.TryLock(key, "")).Should().BeNull();

        await locks.Backend.ForceRelease(key, false);
        (await locks.GetInfo(key)).Should().BeNull();

        await Task.Delay(lockOptions.ExpirationPeriod + TimeSpan.FromSeconds(0.25));
        h.StopToken.IsCancellationRequested.Should().BeTrue();

        await changes.DisposeAsync();
        var changeSet = await changes.Reader.ReadAllAsync().ToHashSetAsync(StringComparer.Ordinal);
        changeSet.Count.Should().Be(1);
        changeSet.Contains(key).Should().BeTrue();
    }

    [Fact(Timeout = 30_000)]
    public async Task ReleaseNotifyTest()
    {
        var locks = _services.GetRequiredService<IMeshLocks<InfrastructureDbContext>>();
        var lockOptions = locks.LockOptions with { ExpirationPeriod = TimeSpan.FromSeconds(10) };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        await using var changes = await locks.Changes("");

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

        await changes.DisposeAsync();
        var changeSet = await changes.Reader.ReadAllAsync().ToHashSetAsync(StringComparer.Ordinal);
        changeSet.Count.Should().Be(1);
        changeSet.Contains(key).Should().BeTrue();
    }
}
