using ActualChat.Mesh;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Mesh;

[Collection(nameof(NonStartingServerCollection)), Trait("Category", nameof(NonStartingServerCollection))]
public class RedisMeshLocksTest(NonStartingAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<NonStartingAppHostFixture>(fixture, @out)
{
    [Fact(Timeout = 30_000)]
    public async Task BasicTest()
    {
        var locks = AppHost.Services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(RedisMeshLocksTest));
        var lockOptions = locks.LockOptions with {
            ExpirationPeriod = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 5 : 2),
        };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        var info = await locks.GetInfo(key);
        info.Should().BeNull();
        (await locks.ListKeys("")).Should().BeEmpty();

        await using (var h = await locks.Lock(key, "", lockOptions)) {
            (await locks.TryLock(key, "")).Should().BeNull();
            (await locks.ListKeys("")).Should().Equal([key]);
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
        var locks = AppHost.Services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(RedisMeshLocksTest));
        var lockOptions = locks.LockOptions with {
            ExpirationPeriod = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 5 : 2),
        };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        await using var changes = await locks.Changes("");
        (await locks.ListKeys("")).Should().BeEmpty();
        (await locks.GetInfo(key)).Should().BeNull();

        await using var h = await locks.Lock(key, "", lockOptions);
        (await locks.TryLock(key, "")).Should().BeNull();
        (await locks.ListKeys("")).Should().Equal([key]);

        await locks.Backend.ForceRelease(key, false);
        (await locks.GetInfo(key)).Should().BeNull();

        var minDelay = TimeSpanExt.Max(locks.UnconditionalCheckPeriod, lockOptions.ExpirationPeriod);
        await Task.Delay(minDelay + TimeSpan.FromSeconds(0.25));
        h.StopToken.IsCancellationRequested.Should().BeTrue();

        await changes.DisposeAsync();
        var changeSet = await changes.Reader.ReadAllAsync().ToHashSetAsync(StringComparer.Ordinal);
        changeSet.Count.Should().Be(1);
        changeSet.Contains(key).Should().BeTrue();
    }

    [Fact(Timeout = 30_000)]
    public async Task ReleaseNotifyTest()
    {
        var locks = AppHost.Services.MeshLocks<InfrastructureDbContext>().WithKeyPrefix(nameof(RedisMeshLocksTest));
        var lockOptions = locks.LockOptions with { ExpirationPeriod = TimeSpan.FromSeconds(10) };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        await using var changes = await locks.Changes("");
        (await locks.ListKeys("")).Should().BeEmpty();
        (await locks.GetInfo(key)).Should().BeNull();

        await using var h1 = await locks.Lock(key, "", lockOptions);
        (await locks.TryLock(key, "")).Should().BeNull();
        (await locks.ListKeys("")).Should().Equal([key]);

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
