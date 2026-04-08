using ActualChat.Testing.Host;
using TimeSpanExt = ActualLab.Time.TimeSpanExt;

namespace ActualChat.Core.Server.IntegrationTests.Mesh;

public class RedisMeshLocksTest(ITestOutputHelper @out)
    : LocalAppHostTestBase($"x-{nameof(RedisMeshLocksTest)}", TestAppHostOptions.None, @out)
{
    [Fact(Timeout = 30_000)]
    public async Task BasicTest()
    {
        var locks = AppHost.Services.MeshLocks().WithKeyPrefix(nameof(RedisMeshLocksTest));
        WriteLine($"{locks.LockOptions}");
        var lockOptions = locks.LockOptions with {
            ExpirationPeriod = TimeSpan.FromSeconds(5),
        };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        var info = await locks.GetInfo(key);
        info.Should().BeNull();
        (await locks.ListKeys("")).Should().BeEmpty();

        var expirationSafetyMargin = TimeSpan.FromSeconds(1);
        await using (var h = await locks.Lock(key, lockOptions)) {
            var now = CpuTimestamp.Now;
            (await locks.TryLock(key)).Should().BeNull();
            (await locks.ListKeys("")).Should().Equal([key]);
            while (now.Elapsed < lockOptions.ExpirationPeriod - expirationSafetyMargin/2) {
                info = await locks.GetInfo(key);
                if (info == null)
                    Assert.Fail($"info == null (elapsed = {now.Elapsed})");
                info.HolderId.Should().Be(h.Id);
                await Task.Delay(TimeSpan.FromSeconds(0.25));
            }
        }

        await Task.Delay(expirationSafetyMargin);
        info = await locks.GetInfo(key);
        info.Should().BeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task LockIsGoneTest()
    {
        var locks = AppHost.Services.MeshLocks().WithKeyPrefix(nameof(RedisMeshLocksTest));
        var lockOptions = locks.LockOptions with {
            ExpirationPeriod = TimeSpan.FromSeconds(TestRunnerInfo.IsBuildAgent() ? 5 : 2),
        };

        Out.WriteLine("Starting test...");
        var key = Alphabet.AlphaNumeric.Generator8.Next();
        (await locks.ListKeys("")).Should().BeEmpty();
        (await locks.GetInfo(key)).Should().BeNull();

        Out.WriteLine("Locking...");
        await using var h = await locks.Lock(key, lockOptions);
        (await locks.TryLock(key)).Should().BeNull();
        (await locks.ListKeys("")).Should().Equal([key]);

        Out.WriteLine("Unlocking...");
        await locks.Backend.ForceRelease(key, false);
        (await locks.GetInfo(key)).Should().BeNull();

        var minDelay = TimeSpanExt.Max(
            locks.LockOptions.UnconditionalCheckPeriod,
            lockOptions.ExpirationPeriod);
        Out.WriteLine($"Waiting for lock to be gone (min delay = {minDelay})...");
        await Task.Delay(minDelay + TimeSpan.FromSeconds(0.5));

        Out.WriteLine("Lock released, checking cancellation token...");
        // We don't silently re-acquire the locks, so it must be gone
        h.StopToken.IsCancellationRequested.Should().BeTrue();
        Out.WriteLine("Test passed.");
    }

    [Fact(Timeout = 30_000)]
    public async Task ReleaseNotifyTest()
    {
        var locks = AppHost.Services.MeshLocks().WithKeyPrefix(nameof(RedisMeshLocksTest));
        var lockOptions = locks.LockOptions with { ExpirationPeriod = TimeSpan.FromSeconds(10) };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        await using var changes = await locks.Changes("");
        (await locks.ListKeys("")).Should().BeEmpty();
        (await locks.GetInfo(key)).Should().BeNull();

        await using var h1 = await locks.Lock(key, lockOptions);
        (await locks.TryLock(key)).Should().BeNull();
        (await locks.ListKeys("")).Should().Equal([key]);

        var h2AcquireTask = locks.Lock(key, lockOptions);
        await Task.Delay(TimeSpan.FromSeconds(0.5)); // WhenChanged needs some time to subscribe
        h2AcquireTask.IsCompleted.Should().BeFalse();

        await h1.DisposeAsync();
        var startedAt = CpuTimestamp.Now;
        await using var h2 = await h2AcquireTask;
        startedAt.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));

        await changes.DisposeAsync();
        var changeSet = await changes.Reader.ReadAllAsync().ToHashSetAsync();
        changeSet.Count.Should().Be(1);
        changeSet.Contains(key).Should().BeTrue();
    }

    [Fact(Timeout = 30_000)]
    public async Task ReleaseAcquireTest()
    {
        var locks = AppHost.Services.MeshLocks().WithKeyPrefix(nameof(ReleaseAcquireTest));
        var lockOptions = locks.LockOptions with { ExpirationPeriod = TimeSpan.FromSeconds(15) };

        var ctsA = new CancellationTokenSource();
        var ctsB = new CancellationTokenSource();
        var key = Alphabet.AlphaNumeric.Generator8.Next();

        (await locks.ListKeys("", CancellationToken.None)).Should().BeEmpty();
        (await locks.GetInfo(key, CancellationToken.None)).Should().BeNull();

        await using (await locks.Lock(key, lockOptions, ctsA.Token)) {
            // The lock must be acquired after entering the "using" block
            (await locks.GetInfo(key, CancellationToken.None)).Should().NotBeNull();

            _ = BackgroundTask.Run(
                () => Task.Delay(1000, CancellationToken.None)
                    .ContinueWith(_ => {
                        ctsA.CancelAndDisposeSilently();
                        // ReSharper disable once AccessToDisposedClosure
                        // await h1.DisposeSilentlyAsync();
                    }, TaskScheduler.Default),
                CancellationToken.None);
        }
        // The lock must be released after leaving the "using" block
        (await locks.GetInfo(key, CancellationToken.None)).Should().BeNull();

        await using (await locks.Lock(key, lockOptions, ctsB.Token))
            (await locks.GetInfo(key, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task RenewalThreadTest()
    {
        var locks = AppHost.Services.MeshLocks().WithKeyPrefix(nameof(RedisMeshLocksTest));
        var expirationPeriod = TimeSpan.FromSeconds(5);
        var lockOptions = locks.LockOptions with {
            ExpirationPeriod = expirationPeriod,
            RenewalPeriodRatio = 0.3f, // Renew every ~1.5s
        };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        // Do not use await using as we are disposing manually at the end of the test to check that renewal works until disposal.
        var h = await locks.Lock(key, lockOptions);

        // Capture initial ExpiresAt
        var initialExpiresAt = h.ExpiresAt;
        var startedAt = CpuTimestamp.Now;
        Out.WriteLine($"Lock acquired: key={key}, expiresAt={initialExpiresAt}");

        // Hold the lock for 2x the expiration period.
        // If the renewal thread doesn't renew, the lock expires after 5s.
        // We hold for 10s, checking periodically that the lock is still alive.
        // Use ExpiresAt as the safe boundary: only check Redis when
        // we know the lock should still be valid (well before current ExpiresAt).
        var holdDuration = expirationPeriod * 2;
        while (startedAt.Elapsed < holdDuration) {
            await Task.Delay(TimeSpan.FromSeconds(1));

            h.StopToken.IsCancellationRequested.Should().BeFalse(
                "holder should not be stopped at {0}", startedAt.Elapsed);
            h.IsExpiredOnRenewal.Should().BeFalse(
                "holder should not report expiration at {0}", startedAt.Elapsed);

            var info = await locks.GetInfo(key);
            info.Should().NotBeNull(
                "lock should still be held at {0} (expiration={1})", startedAt.Elapsed, expirationPeriod);
            info!.HolderId.Should().Be(h.Id);

            Out.WriteLine($"  {startedAt.Elapsed}: lock still held, expiresAt={h.ExpiresAt}");
        }

        // ExpiresAt must have advanced beyond the initial value (proving renewal happened)
        h.ExpiresAt.Should().BeGreaterThan(initialExpiresAt,
            "ExpiresAt should advance as the renewal thread renews the lock");
        Out.WriteLine($"Final expiresAt={h.ExpiresAt} (initial was {initialExpiresAt})");

        // Clean disposal — lock is released, not expired
        await h.DisposeAsync();
        h.IsExpiredOnRenewal.Should().BeFalse();

        await Task.Delay(TimeSpan.FromSeconds(0.5));
        var finalInfo = await locks.GetInfo(key);
        finalInfo.Should().BeNull("lock should be released after disposal");
    }

    [Fact(Skip = "For manual runs only. Start/stop Redis and watch the output.")]
    public async Task RedisReconnectTest()
    {
        var locks = AppHost.Services.MeshLocks().WithKeyPrefix(nameof(RedisMeshLocksTest));
        var lockOptions = locks.LockOptions with {
            ExpirationPeriod = TimeSpan.FromSeconds(2),
        };

        var key = Alphabet.AlphaNumeric.Generator8.Next();
        while (true) {
            WriteLine("Locking...");
            try {
                await using (var h = await locks.Lock(key, lockOptions)) {
                    WriteLine("Locked.");
                    await TaskExt.NeverEnding(h.StopToken).SilentAwait();
                }
                WriteLine("Unlocked.");
            }
            catch (Exception e) {
                WriteLine($"Locking failed: {e}");
            }
        }
    }
}
