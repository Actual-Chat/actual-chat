using ActualChat.Pool;
using Stl.Time.Testing;

namespace ActualChat.Core.UnitTests;

public class SharedPoolTest
{
    [Fact]
    public async Task BasicTest()
    {
        var testClock = new TestClock();
        var cts = new CancellationTokenSource();
        var pool = new SharedPool<int, Val>(Factory, 0);
        using (_ = await pool.Lease(10)) {
            using var __ = await pool.Lease(10);
        }
        cts.IsCancellationRequested.Should().Be(true);

        async Task<Val> Factory(int _)
        {
            await testClock.Delay(100);
            return new Val(cts);
        }
    }

    [Fact]
    public async Task DisposeDelayTest()
    {
        var testClock = new TestClock();
        var cts = new CancellationTokenSource();
        var pool = new SharedPool<int, Val>(Factory, 0.5);
        using (_ = await pool.Lease(10)) {
            using var __ = await pool.Lease(10);
        }
        cts.IsCancellationRequested.Should().Be(false);

        await testClock.Delay(1000).ConfigureAwait(false);

        cts.IsCancellationRequested.Should().Be(true);

        async Task<Val> Factory(int _)
        {
            await testClock.Delay(100);
            return new Val(cts);
        }
    }

    [Fact]
    public async Task LeaseBeforeDisposeDelayTest()
    {
        var testClock = new TestClock();
        var cts = new CancellationTokenSource();
        var pool = new SharedPool<int, Val>(Factory, 0.5);
        using (_ = await pool.Lease(10)) {
            using var __ = await pool.Lease(10);
        }
        cts.IsCancellationRequested.Should().Be(false);

        using (_ = await pool.Lease(10)) {
            await testClock.Delay(1000).ConfigureAwait(false);

            cts.IsCancellationRequested.Should().Be(false);
        }

        await testClock.Delay(1000).ConfigureAwait(false);

        cts.IsCancellationRequested.Should().Be(true);

        async Task<Val> Factory(int _)
        {
            await testClock.Delay(100);
            return new Val(cts);
        }
    }

    public class Val : IDisposable
    {
        private readonly CancellationTokenSource _cts;

        public Val(CancellationTokenSource cts)
            => _cts = cts;

        public void Dispose()
            => _cts.Cancel();
    }
}
