using ActualChat.Pooling;
using ActualLab.Time.Testing;

namespace ActualChat.Core.UnitTests;

public class SharedResourcePoolTest : TestBase
{
    public SharedResourcePoolTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task BasicTest()
    {
        var cancellationToken = CancellationToken.None;
        using var testClock = new TestClock();
        var pool = new SharedResourcePool<int, Resource>(ResourceFactory) {
            ResourceDisposeDelay = TimeSpan.Zero,
        };

        var l = await pool.Rent(10, cancellationToken);
        using (var l1 = l) {
            l.IsRented.Should().BeTrue();
            l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();

            using var l2 = await pool.Rent(10, cancellationToken);
            l2.Should().BeSameAs(l);
            l.IsRented.Should().BeTrue();
            l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();
        }
        l.IsRented.Should().BeFalse();

        await testClock.Delay(100, cancellationToken);
        l.Resource.WhenDisposed.IsCompleted.Should().BeTrue();
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

        await testClock.Delay(1000, cancellationToken);
        l.Resource.WhenDisposed.IsCompleted.Should().BeTrue();
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

        await testClock.Delay(2000, cancellationToken);
        l.Resource.WhenDisposed.IsCompleted.Should().BeTrue();
    }

    [Fact (Timeout = 5000)]
    public async Task ShouldNotStuckWhenCancellationTokenIsFired()
    {
        // When resource factory task fails asynchronously with non-transient error,
        // it causes that pool infinitely tries to execute Lease.BeginRent.
        async Task<Resource> ResourceFactory1(int _, CancellationToken cancellationToken) {
            await Task.Delay(1000, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new Resource();
        }

        using var cancellationTokenSource = new CancellationTokenSource(200);
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
