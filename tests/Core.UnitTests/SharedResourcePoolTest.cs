using ActualChat.Pooling;
using ActualLab.Time.Testing;

namespace ActualChat.Core.UnitTests;

public class SharedResourcePoolTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task BasicTest()
    {
        var cancellationToken = CancellationToken.None;
        using var testClock = new TestClock();
        var pool = new SharedResourcePool<int, Resource>(ResourceFactory) {
            ResourceDisposeDelay = TimeSpan.Zero,
        };

        var l = await pool.Rent(10, cancellationToken);
        using (var _ = l) {
            l.IsRented.Should().BeTrue();
            l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();

            using var l2 = await pool.Rent(10, cancellationToken);
            l2.Should().BeSameAs(l);
            l.IsRented.Should().BeTrue();
            l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();
        }
        l.IsRented.Should().BeFalse();

        await l.Resource.WhenDisposed.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    [Fact]
    public async Task DisposeDelayTest()
    {
        var cancellationToken = CancellationToken.None;
        using var testClock = new TestClock();
        var pool = new SharedResourcePool<int, Resource>(ResourceFactory) {
            ResourceDisposeDelay = TimeSpan.FromSeconds(0.5),
        };

        var l = await pool.Rent(10, cancellationToken);
        using (var l1 = l) {
            l.IsRented.Should().BeTrue();
            l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();

            using var l2 = await pool.Rent(10, cancellationToken);
            l.Should().BeSameAs(l);
            l.IsRented.Should().BeTrue();
            l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();
        }
        l.IsRented.Should().BeFalse();
        l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();

        await l.Resource.WhenDisposed.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    [Fact]
    public async Task DisposeDelayCancellationTest()
    {
        var cancellationToken = CancellationToken.None;
        using var testClock = new TestClock();
        var pool = new SharedResourcePool<int, Resource>(ResourceFactory) {
            ResourceDisposeDelay = TimeSpan.FromSeconds(0.2),
        };

        var l = await pool.Rent(10, cancellationToken);
        using (var l1 = l) {
            l.IsRented.Should().BeTrue();
            l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();

            using var l2 = await pool.Rent(10, cancellationToken);
            l.Should().BeSameAs(l);
            l.IsRented.Should().BeTrue();
            l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();
        }
        l.IsRented.Should().BeFalse();
        l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();

        using (var l3 = await pool.Rent(10, cancellationToken)) {
            l3.Should().BeSameAs(l);
            l.IsRented.Should().BeTrue();
            l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();

            await testClock.Delay(500, cancellationToken);
            l.IsRented.Should().BeTrue();
            l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();
        }
        l.IsRented.Should().BeFalse();

        await l.Resource.WhenDisposed.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task DisposeShouldDisposeResourceCreatedDuringDispose()
    {
        // Verifies the multi-round dispose: when a factory finishes mid-dispose,
        // the produced resource is picked up by a subsequent round and disposed.

        var factoryStarted = new TaskCompletionSource();
        var factoryGate = new TaskCompletionSource<Resource>();
        var producedResource = new Resource();

        async Task<Resource> SlowFactory(int _, CancellationToken ct) {
            factoryStarted.TrySetResult();
            // Ignore cancellation: we want the factory to deliver a resource that
            // shows up in a later dispose round.
            return await factoryGate.Task.ConfigureAwait(false);
        }

        var pool = new SharedResourcePool<int, Resource>(SlowFactory) {
            ResourceDisposeDelay = TimeSpan.Zero,
        };

        using var rentCts = new CancellationTokenSource();
        var rentTask = Task.Run(async () => {
            try {
                using var lease = await pool.Rent(10, rentCts.Token).ConfigureAwait(false);
                return true;
            }
            catch {
                return false;
            }
        });

        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposeTask = pool.DisposeAsync().AsTask();
        // Let dispose start and observe the in-flight lease in its first round(s).
        await Task.Delay(150);
        // Now hand the factory a resource — a later dispose round should pick it up.
        factoryGate.TrySetResult(producedResource);

        await disposeTask.WaitAsync(TimeSpan.FromSeconds(3));
        producedResource.WhenDisposed.IsCompleted.Should().BeTrue();

        rentCts.Cancel();
        await rentTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact(Timeout = 5000)]
    public async Task DisposeShouldCancelInFlightFactory()
    {
        // Verifies the pool-level cancellation: an in-flight factory honoring its
        // cancellation token gets cancelled by DisposeAsync, the lease self-cleans,
        // and dispose completes before the wall-clock timeout.

        var factoryStarted = new TaskCompletionSource();
        var factoryCancelled = new TaskCompletionSource();

        async Task<Resource> CancellableFactory(int _, CancellationToken ct) {
            factoryStarted.TrySetResult();
            try {
                await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                return new Resource();
            }
            catch (OperationCanceledException) {
                factoryCancelled.TrySetResult();
                throw;
            }
        }

        var pool = new SharedResourcePool<int, Resource>(CancellableFactory) {
            ResourceDisposeDelay = TimeSpan.Zero,
        };

        var rentTask = Task.Run(async () => {
            try {
                using var lease = await pool.Rent(10).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) {
                return false;
            }
        });

        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await pool.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        await factoryCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await rentTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact(Timeout = 5000)]
    public async Task DisposeShouldCancelPendingDelayedEndRent()
    {
        var disposeCount = 0;
        var pool = new SharedResourcePool<int, Resource>(
            ResourceFactory,
            (_, resource) => {
                Interlocked.Increment(ref disposeCount);
                resource.Dispose();
                return default;
            }) {
            ResourceDisposeDelay = TimeSpan.FromMilliseconds(100),
        };

        var lease = await pool.Rent(10);
        lease.Dispose();

        await pool.DisposeAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        disposeCount.Should().Be(1);
    }

    [Fact(Timeout = 5000)]
    public async Task DisposeShouldNotHangWhenResourceFactoryIsInFlight()
    {
        // Reproduces the dispose deadlock: Lease.Resource does a synchronous
        // _resourceTask.GetAwaiter().GetResult(), so SharedResourcePool.DisposeAsync
        // blocks forever when a Rent's factory is still in flight at disposal time.

        var factoryStarted = new TaskCompletionSource();
        var factoryGate = new TaskCompletionSource<Resource>();

        async Task<Resource> SlowFactory(int _, CancellationToken ct) {
            factoryStarted.TrySetResult();
            using var reg = ct.Register(() => factoryGate.TrySetCanceled(ct));
            return await factoryGate.Task.ConfigureAwait(false);
        }

        var pool = new SharedResourcePool<int, Resource>(SlowFactory) {
            ResourceDisposeDelay = TimeSpan.Zero,
        };

        using var rentCts = new CancellationTokenSource();
        var rentTask = Task.Run(async () => {
            try {
                using var lease = await pool.Rent(10, rentCts.Token).ConfigureAwait(false);
                return true;
            }
            catch {
                return false;
            }
        });

        // Wait until the factory is invoked — at this point the lease is in _leases
        // and _resourceTask is running but not completed.
        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // This is the key assertion: DisposeAsync must complete promptly even though
        // a Rent's factory is in flight. Without the fix, the pool blocks on
        // Lease.Resource's sync .GetAwaiter().GetResult() forever.
        var disposeTask = pool.DisposeAsync().AsTask();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));

        // Cleanup — unblock the factory and the pending rent.
        rentCts.Cancel();
        factoryGate.TrySetCanceled();
        await rentTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [FlakyFact("AY: Time-dependent", 3, Timeout = 5000)]
    public async Task ShouldNotStuckWhenCancellationTokenIsFired()
    {
        // When a resource factory task fails asynchronously with a non-transient error,
        // it causes that pool infinitely tries to execute Lease.BeginRent.
        async Task<Resource> ResourceFactory1(int _, CancellationToken cancellationToken) {
            await Task.Delay(1500, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new Resource();
        }

        using var cancellationTokenSource = new CancellationTokenSource(100);
        var cancellationToken = cancellationTokenSource.Token;
        var pool = new SharedResourcePool<int, Resource>(ResourceFactory1) {
            ResourceDisposeDelay = TimeSpan.Zero,
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pool.Rent(10, cancellationToken));
    }

    private Task<Resource> ResourceFactory(int _, CancellationToken cancellationToken)
        => Task.FromResult(new Resource());

    private sealed class Resource : IDisposable
    {
        private readonly TaskCompletionSource _whenDisposed = new();

        public Task WhenDisposed => _whenDisposed.Task;

        public void Dispose()
            => _whenDisposed.TrySetResult();
    }
}
