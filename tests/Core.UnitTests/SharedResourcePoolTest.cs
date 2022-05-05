using ActualChat.Pooling;
using Stl.Time.Testing;

namespace ActualChat.Core.UnitTests;

public class SharedResourcePoolTest : TestBase
{
    public SharedResourcePoolTest(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public async Task BasicTest()
    {
        var cancellationToken = CancellationToken.None;
        var testClock = new TestClock();
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
        var testClock = new TestClock();
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
        var testClock = new TestClock();
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

        using (var l3 = await pool.Rent(10, cancellationToken)) {
            l3.Should().BeSameAs(l);
            l.IsRented.Should().BeTrue();
            l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();

            await testClock.Delay(1000, cancellationToken);
            l.IsRented.Should().BeTrue();
            l.Resource.WhenDisposed.IsCompleted.Should().BeFalse();
        }
        l.IsRented.Should().BeFalse();

        await testClock.Delay(1000, cancellationToken);
        l.Resource.WhenDisposed.IsCompleted.Should().BeTrue();

    }

    private Task<Resource> ResourceFactory(int _, CancellationToken cancellationToken)
        => Task.FromResult(new Resource());

    private sealed class Resource : IDisposable
    {
        private readonly TaskSource<Unit> _whenDisposed = TaskSource.New<Unit>(false);

        public Task WhenDisposed => _whenDisposed.Task;

        public void Dispose()
            => _whenDisposed.TrySetResult(default);
    }
}
