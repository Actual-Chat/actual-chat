using ActualChat.Pooling;
using ActualLab.Time.Testing;

namespace ActualChat.Core.UnitTests;

public class SharedResourcePoolTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const int TestTimeoutMs = 30_000;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task PoolShouldShareResourceAndDisposeItAfterLastRent()
    {
        // arrange
        var cancellationToken = CancellationToken.None;
        using var testClock = new TestClock();
        var pool = new SharedResourcePool<int, Resource>(ResourceFactory) {
            ResourceDisposeDelay = TimeSpan.Zero,
        };

        // act & assert
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
        await l.Resource.WhenDisposed.WaitAsync(WaitTimeout, cancellationToken);
    }

    [Fact]
    public async Task ResourceDisposeDelayShouldPostponeDisposal()
    {
        // arrange
        var cancellationToken = CancellationToken.None;
        using var testClock = new TestClock();
        var pool = new SharedResourcePool<int, Resource>(ResourceFactory) {
            ResourceDisposeDelay = TimeSpan.FromSeconds(0.5),
        };

        // act & assert
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
        await l.Resource.WhenDisposed.WaitAsync(WaitTimeout, cancellationToken);
    }

    [Fact]
    public async Task NewRentShouldCancelPendingDelayedDisposal()
    {
        // arrange
        var cancellationToken = CancellationToken.None;
        using var testClock = new TestClock();
        var pool = new SharedResourcePool<int, Resource>(ResourceFactory) {
            ResourceDisposeDelay = TimeSpan.FromSeconds(0.2),
        };

        // act & assert
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
        await l.Resource.WhenDisposed.WaitAsync(WaitTimeout, cancellationToken);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task DisposeShouldDisposeResourceCreatedDuringDispose()
    {
        // arrange
        var factoryStarted = TaskCompletionSourceExt.New();
        var factoryGate = TaskCompletionSourceExt.New<Resource>();
        var producedResource = new Resource();

        async Task<Resource> SlowFactory(int _, CancellationToken ct) {
            // Ignores cancellation, so its resource surfaces in a later dispose round
            factoryStarted.TrySetResult();
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

        // act
        await factoryStarted.Task.WaitAsync(WaitTimeout);
        var disposeTask = pool.DisposeAsync().AsTask();
        // Lets dispose observe the in-flight lease in its first round(s)
        await Task.Delay(150);
        factoryGate.TrySetResult(producedResource);

        // assert
        await disposeTask.WaitAsync(WaitTimeout);
        producedResource.WhenDisposed.IsCompleted.Should().BeTrue();

        rentCts.Cancel();
        await rentTask.WaitAsync(WaitTimeout);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task DisposeShouldCancelInFlightFactory()
    {
        // arrange
        var factoryStarted = TaskCompletionSourceExt.New();
        var factoryCancelled = TaskCompletionSourceExt.New();

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

        // act
        await factoryStarted.Task.WaitAsync(WaitTimeout);

        // assert
        await pool.DisposeAsync().AsTask().WaitAsync(WaitTimeout);
        await factoryCancelled.Task.WaitAsync(WaitTimeout);
        await rentTask.WaitAsync(WaitTimeout);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task DisposeShouldCancelPendingDelayedEndRent()
    {
        // arrange
        var disposeDelay = TimeSpan.FromMilliseconds(100);
        var disposeCount = 0;
        var pool = new SharedResourcePool<int, Resource>(
            ResourceFactory,
            (_, resource) => {
                Interlocked.Increment(ref disposeCount);
                resource.Dispose();
                return default;
            }) {
            ResourceDisposeDelay = disposeDelay,
        };

        // act
        var lease = await pool.Rent(10);
        lease.Dispose();
        await pool.DisposeAsync();
        // Gives the delayed EndRent scheduled by lease.Dispose() a chance to misfire
        await Task.Delay(disposeDelay * 3);

        // assert
        disposeCount.Should().Be(1, "the resource must be disposed exactly once");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task DisposeShouldNotHangWhenResourceFactoryIsInFlight()
    {
        // arrange
        var factoryStarted = TaskCompletionSourceExt.New();
        var factoryGate = TaskCompletionSourceExt.New<Resource>();

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

        // act
        // Once the factory is invoked, the lease is in _leases with a running _resourceTask
        await factoryStarted.Task.WaitAsync(WaitTimeout);
        var disposeTask = pool.DisposeAsync().AsTask();

        // assert
        // Regression: Lease.Resource's sync .GetAwaiter().GetResult() used to block this forever
        await disposeTask.WaitAsync(WaitTimeout);

        rentCts.Cancel();
        factoryGate.TrySetCanceled();
        await rentTask.WaitAsync(WaitTimeout);
    }

    [FlakyFact("AY: Time-dependent", 3, Timeout = TestTimeoutMs)]
    public async Task ShouldNotStuckWhenCancellationTokenIsFired()
    {
        // arrange
        // A resource factory failing asynchronously with a non-transient error used to
        // make the pool retry Lease.BeginRent forever.
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

        // act
        var rent = () => pool.Rent(10, cancellationToken).AsTask();

        // assert
        await rent.Should().ThrowAsync<OperationCanceledException>();
    }

    // Private methods

    private Task<Resource> ResourceFactory(int _, CancellationToken cancellationToken)
        => Task.FromResult(new Resource());

    // Nested types

    private sealed class Resource : IDisposable
    {
        private readonly TaskCompletionSource _whenDisposed = TaskCompletionSourceExt.New();
        public Task WhenDisposed => _whenDisposed.Task;

        public void Dispose()
            => _whenDisposed.TrySetResult();
    }
}
