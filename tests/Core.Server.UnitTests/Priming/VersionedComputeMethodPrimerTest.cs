namespace ActualChat.Core.Server.UnitTests.Priming;

public class VersionedComputeMethodPrimerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly Func<string, CancellationToken, Task<int>> NoopCaller
        = (_, _) => Task.FromResult(0);

    [Fact]
    public async Task PrimeGetRoundtrip()
    {
        var primer = new VersionedComputeMethodPrimer<string, long, int>(NoopCaller);

        // Noop caller doesn't consume, so the primed value sticks around until TryGetPrimedValue
        await primer.Prime("a", 1, 42);

        primer.TryUsePrimed("a", out var v).Should().BeTrue();
        v.Should().Be(42);

        // Consumed — second get returns false.
        primer.TryUsePrimed("a", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CallerConsumesPrimedValue()
    {
        VersionedComputeMethodPrimer<string, long, int>? primer = null;
        var consumed = -1;
        primer = new VersionedComputeMethodPrimer<string, long, int>(
            (key, _) => {
                if (primer!.TryUsePrimed(key, out var x))
                    consumed = x;
                return Task.FromResult(consumed);
            });

        await primer.Prime("a", 1, 42);
        consumed.Should().Be(42);
        // Caller consumed during Prime, so the slot is back to empty.
        primer.TryUsePrimed("a", out _).Should().BeFalse();
    }

    [Fact]
    public async Task LowerVersionIsRejected()
    {
        var callerInvocations = 0;
        var primer = new VersionedComputeMethodPrimer<string, long, int>(
            (_, _) => { Interlocked.Increment(ref callerInvocations); return Task.FromResult(0); });

        await primer.Prime("a", 5, 100);
        callerInvocations.Should().Be(2); // one in Invalidation.Begin, one outside

        // Lower version — rejected, no caller invocation.
        await primer.Prime("a", 3, 999);
        callerInvocations.Should().Be(2);

        primer.TryUsePrimed("a", out var v).Should().BeTrue();
        v.Should().Be(100);
    }

    [Fact]
    public async Task EqualVersionIsRejected()
    {
        var primer = new VersionedComputeMethodPrimer<string, long, int>(NoopCaller);

        await primer.Prime("a", 5, 100);
        await primer.Prime("a", 5, 999); // same version — rejected

        primer.TryUsePrimed("a", out var v).Should().BeTrue();
        v.Should().Be(100);
    }

    [Fact]
    public async Task HigherVersionReplaces()
    {
        var primer = new VersionedComputeMethodPrimer<string, long, int>(NoopCaller);

        await primer.Prime("a", 1, 100);
        await primer.Prime("a", 2, 200);

        primer.TryUsePrimed("a", out var v).Should().BeTrue();
        v.Should().Be(200);
    }

    [Fact]
    public async Task VersionFloorSurvivesConsume()
    {
        var primer = new VersionedComputeMethodPrimer<string, long, int>(NoopCaller);

        await primer.Prime("a", 5, 100);
        primer.TryUsePrimed("a", out var v1).Should().BeTrue();
        v1.Should().Be(100);

        // After consume, lower-or-equal versions are still rejected.
        await primer.Prime("a", 5, 999);
        primer.TryUsePrimed("a", out _).Should().BeFalse();

        // Higher version accepted again.
        await primer.Prime("a", 6, 300);
        primer.TryUsePrimed("a", out var v2).Should().BeTrue();
        v2.Should().Be(300);
    }

    [Fact]
    public async Task MissingKeyReturnsFalse()
    {
        var primer = new VersionedComputeMethodPrimer<string, long, int>(NoopCaller);
        primer.TryUsePrimed("missing", out _).Should().BeFalse();
    }

    [Fact]
    public async Task DifferentKeysAreIndependent()
    {
        var primer = new VersionedComputeMethodPrimer<string, long, int>(NoopCaller);

        await primer.Prime("a", 1, 10);
        await primer.Prime("b", 1, 20);

        primer.TryUsePrimed("a", out var va).Should().BeTrue();
        va.Should().Be(10);
        primer.TryUsePrimed("b", out var vb).Should().BeTrue();
        vb.Should().Be(20);
    }

    [Fact]
    public async Task EntrySelfEvictsAfterLifetime()
    {
        var primer = new VersionedComputeMethodPrimer<string, long, int>(NoopCaller) {
            EntryLifetime = TimeSpan.FromMilliseconds(300),
        };

        await primer.Prime("a", 5, 100);
        primer.TryUsePrimed("a", out var v).Should().BeTrue();
        v.Should().Be(100);

        // The empty entry keeps the version floor until it self-evicts after EntryLifetime.
        // Eviction runs on the shared timer set with no upper-bound firing guarantee, so poll:
        // a lower version is accepted only once the slot is fresh.
        await TestExt.When(async () => {
            await primer.Prime("a", 1, 7);
            primer.TryUsePrimed("a", out var v2).Should().BeTrue();
            v2.Should().Be(7);
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task ConcurrentPrimesHighestVersionWins()
    {
        var primer = new VersionedComputeMethodPrimer<string, long, int>(NoopCaller);

        var tasks = Enumerable.Range(1, 50)
            .Select(i => primer.Prime("k", i, i * 10))
            .ToArray();
        await Task.WhenAll(tasks);

        primer.TryUsePrimed("k", out var v).Should().BeTrue();
        v.Should().Be(500); // i=50 → value 500
    }
}
