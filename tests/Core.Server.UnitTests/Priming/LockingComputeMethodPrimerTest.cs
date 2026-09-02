using ActualLab.Locking;

namespace ActualChat.Core.Server.UnitTests.Priming;

public class LockingComputeMethodPrimerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Func<string, CancellationToken, Task<int>> NoopCaller
        = (_, _) => Task.FromResult(0);

    [Fact]
    public async Task PrimeGetRoundtrip()
    {
        var primer = new LockingComputeMethodPrimer<string, int>(NoopCaller);

        using var r = await primer.LockAndPrepare("a");
        r.Key.Should().Be("a");
        r.HasValue.Should().BeFalse();
        primer.GetReservationCount().Should().Be(1);

        primer.TryUsePrimed("a", out _).Should().BeFalse();

        // NoopCaller doesn't pull, so state remains HasValue after Prime completes
        await r.Prime(42);
        r.HasValue.Should().BeTrue();

        primer.TryUsePrimed("a", out var v1).Should().BeTrue();
        v1.Should().Be(42);

        // TryGetPrimedValue flips state back to Empty but keeps the slot until dispose
        r.HasValue.Should().BeFalse();
        primer.GetReservationCount().Should().Be(1);
        primer.TryUsePrimed("a", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CallerConsumesPrimedValue()
    {
        LockingComputeMethodPrimer<string, int>? primer = null;
        var consumed = -1;
        primer = new LockingComputeMethodPrimer<string, int>(
            (key, _) => {
                if (primer!.TryUsePrimed(key, out var v))
                    consumed = v;
                return Task.FromResult(consumed);
            });

        using var r = await primer.LockAndPrepare("a");
        await r.Prime(42);
        consumed.Should().Be(42);
        // Caller consumed during Prime, so the slot is back to Empty
        r.HasValue.Should().BeFalse();
    }

    [Fact]
    public async Task DisposeRemovesSlotAndReleasesLock()
    {
        var primer = new LockingComputeMethodPrimer<string, int>(NoopCaller, LockReentryMode.Unchecked);

        var r = await primer.LockAndPrepare("k");
        primer.GetReservationCount().Should().Be(1);
        r.Dispose();
        primer.GetReservationCount().Should().Be(0);

        r.Dispose();
        primer.GetReservationCount().Should().Be(0);

        using var r2 = await primer.LockAndPrepare("k").AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        r2.Key.Should().Be("k");
    }

    [Fact]
    public async Task DisposedReservationDoesNotRemoveFreshOne()
    {
        var primer = new LockingComputeMethodPrimer<string, int>(NoopCaller, LockReentryMode.Unchecked);

        var r1 = await primer.LockAndPrepare("k");
        await r1.Prime(7);
        primer.TryUsePrimed("k", out var v).Should().BeTrue();
        v.Should().Be(7);
        r1.Dispose();

        using var r2 = await primer.LockAndPrepare("k");
        primer.GetReservationCount().Should().Be(1);
        r1.Dispose(); // Second dispose on stale r1 must not touch r2's slot
        primer.GetReservationCount().Should().Be(1);
    }

    [Fact]
    public async Task PrimeAfterDisposeThrows()
    {
        var primer = new LockingComputeMethodPrimer<string, int>(NoopCaller);

        var r = await primer.LockAndPrepare("a");
        r.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await r.Prime(1));
    }

    [Fact]
    public void GetMissingKeyReturnsFalse()
    {
        var primer = new LockingComputeMethodPrimer<string, int>(NoopCaller);
        primer.TryUsePrimed("missing", out _).Should().BeFalse();
    }

    [Fact]
    public async Task PerKeyLockSerializesReserves()
    {
        var primer = new LockingComputeMethodPrimer<string, int>(NoopCaller, LockReentryMode.Unchecked);
        var r1 = await primer.LockAndPrepare("k");

        var second = primer.LockAndPrepare("k").AsTask();
        await Task.Delay(50);
        second.IsCompleted.Should().BeFalse();

        r1.Dispose();
        var r2 = await second.WaitAsync(TimeSpan.FromSeconds(10));
        r2.Key.Should().Be("k");
        r2.Dispose();
    }

    [Fact]
    public async Task DifferentKeysDoNotBlock()
    {
        var primer = new LockingComputeMethodPrimer<string, int>(NoopCaller, LockReentryMode.Unchecked);
        using var r1 = await primer.LockAndPrepare("a");
        using var r2 = await primer.LockAndPrepare("b").AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        primer.GetReservationCount().Should().Be(2);
    }

    [Fact]
    public async Task SharedLockSetCtor()
    {
        var locks = new AsyncLockSet<string>(LockReentryMode.Unchecked);
        var primer = new LockingComputeMethodPrimer<string, int>(NoopCaller, locks);

        using var r = await primer.LockAndPrepare("a");
        await r.Prime(5);
        primer.TryUsePrimed("a", out var v).Should().BeTrue();
        v.Should().Be(5);
    }
}
