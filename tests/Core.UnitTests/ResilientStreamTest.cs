using ActualLab.Resilience;

namespace ActualChat.Core.UnitTests;

public class ResilientStreamTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task NormalCompletion()
    {
        var items = new[] { 1, 2, 3, 4, 5 };
        var stream = new ResilientStream<int> {
            Provider = _ => Task.FromResult(items.ToAsyncEnumerable()),
        };

        var result = await stream.ToListAsync();
        result.Should().Equal(items);
    }

    [Fact]
    public async Task RetryOnTransientError()
    {
        var attempt = 0;
        var stream = new ResilientStream<int> {
            Provider = _ => {
                attempt++;
                if (attempt == 1)
                    return Task.FromResult(FailAfter(new[] { 1, 2 }, new InvalidOperationException("transient")));
                return Task.FromResult(new[] { 3, 4, 5 }.ToAsyncEnumerable());
            },
            RetryPolicy = new RetryPolicy(3, RetryDelaySeq.Zero),
        };

        var result = await stream.ToListAsync();
        result.Should().Equal(1, 2, 3, 4, 5);
        attempt.Should().Be(2);
    }

    [Fact]
    public async Task ResetItemOnRetry()
    {
        var attempt = 0;
        var stream = new ResilientStream<int> {
            Provider = _ => {
                attempt++;
                if (attempt == 1)
                    return Task.FromResult(FailAfter(new[] { 1, 2 }, new InvalidOperationException("transient")));
                return Task.FromResult(new[] { 3, 4 }.ToAsyncEnumerable());
            },
            RetryPolicy = new RetryPolicy(3, RetryDelaySeq.Zero),
            ResetItem = Option.Some(-1),
        };

        var result = await stream.ToListAsync();
        // ResetItem is written only before retry attempts, not the first one
        result.Should().Equal(1, 2, -1, 3, 4);
    }

    [Fact(Timeout = 15_000)]
    public async Task BreakReconnectsWithoutEndingTheStream()
    {
        // arrange
        var attempt = 0;
        var stream = null as ResilientStream<int>;
        stream = new ResilientStream<int> {
            Provider = ct => {
                attempt++;
                return Task.FromResult(attempt == 1
                    ? HangAfter([1], ct)
                    : HangAfter([2, 3], ct));
            },
            RetryPolicy = new RetryPolicy(3, RetryDelaySeq.Zero),
            ResetItem = Option.Some(-1),
            IsInfinite = true,
        };

        // act
        var result = new List<int>();
        var consumeTask = Task.Run(async () => {
            await foreach (var item in stream) {
                result.Add(item);
                if (item == 1)
                    stream.Break();
                if (item == 3)
                    break;
            }
        });
        await consumeTask.WaitAsync(TimeSpan.FromSeconds(5));

        // assert
        result.Should().Equal(1, -1, 2, 3);
        attempt.Should().Be(2);
    }

    [Fact]
    public async Task NonRetriableError()
    {
        var terminalError = new InvalidOperationException("terminal");
        var stream = new ResilientStream<int> {
            Provider = _ => Task.FromResult(FailAfter(new[] { 1 }, terminalError)),
            RetryPolicy = new RetryPolicy(1, RetryDelaySeq.Zero), // No retries (1 try total)
        };

        var result = new List<int>();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await foreach (var item in stream)
                result.Add(item);
        });
        ex.Should().BeSameAs(terminalError);
        result.Should().Equal(1);
    }

    [Fact]
    public async Task MultipleEnumeration()
    {
        var callCount = 0;
        var stream = new ResilientStream<int> {
            Provider = _ => {
                Interlocked.Increment(ref callCount);
                return Task.FromResult(new[] { 1, 2, 3 }.ToAsyncEnumerable());
            },
        };

        var result1 = await stream.ToListAsync();
        var result2 = await stream.ToListAsync();
        result1.Should().Equal(1, 2, 3);
        result2.Should().Equal(1, 2, 3);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task GetChannelReturnsFunctionalChannel()
    {
        var stream = new ResilientStream<int> {
            Provider = _ => Task.FromResult(new[] { 10, 20, 30 }.ToAsyncEnumerable()),
        };

        var channel = stream.GetChannel();
        var result = await channel.Reader.ReadAllAsync().ToListAsync();
        result.Should().Equal(10, 20, 30);
    }

    [Fact]
    public async Task SourceFactoryThrows()
    {
        var attempt = 0;
        var stream = new ResilientStream<int> {
            Provider = _ => {
                attempt++;
                if (attempt <= 2)
                    throw new InvalidOperationException("factory failure");
                return Task.FromResult(new[] { 1, 2 }.ToAsyncEnumerable());
            },
            RetryPolicy = new RetryPolicy(5, RetryDelaySeq.Zero),
        };

        var result = await stream.ToListAsync();
        result.Should().Equal(1, 2);
        attempt.Should().Be(3);
    }

    [Fact]
    public async Task CancellationDuringStreaming()
    {
        using var cts = new CancellationTokenSource();
        var stream = new ResilientStream<int> {
            Provider = _ => Task.FromResult(SlowStream(cts)),
        };

        var result = new List<int>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            await foreach (var item in stream.WithCancellation(cts.Token))
                result.Add(item);
        });
        result.Should().Contain(1);
    }

    [Fact]
    public async Task RetriesExhausted()
    {
        var attempt = 0;
        var stream = new ResilientStream<int> {
            Provider = _ => {
                attempt++;
                return Task.FromResult(FailAfter(Array.Empty<int>(), new InvalidOperationException($"fail {attempt}")));
            },
            RetryPolicy = new RetryPolicy(3, RetryDelaySeq.Zero),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await foreach (var _ in stream) { }
        });
        ex.Message.Should().Be("fail 3");
        attempt.Should().Be(3);
    }

    [Fact]
    public async Task CancellationTokenProperty()
    {
        using var cts = new CancellationTokenSource();
        var stream = new ResilientStream<int> {
            Provider = _ => Task.FromResult(SlowStream(cts)),
            CancellationToken = cts.Token,
        };

        var result = new List<int>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => {
            // No WithCancellation here — the property-based token should work
            await foreach (var item in stream)
                result.Add(item);
        });
        result.Should().Contain(1);
    }

    [Fact]
    public async Task EmptySourceCompletes()
    {
        var stream = new ResilientStream<int> {
            Provider = _ => Task.FromResult(AsyncEnumerable.Empty<int>()),
        };

        var result = await stream.ToListAsync();
        result.Should().BeEmpty();
    }

    // Private methods

    // Hangs rather than completing, so an IsInfinite stream doesn't reconnect on its own and
    // the test observes only the reconnect Break() caused.
    private static async IAsyncEnumerable<int> HangAfter(
        int[] values,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var value in values)
            yield return value;

        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<int> FailAfter(
        int[] items,
        Exception error,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items) {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
        throw error;
    }

    private static async IAsyncEnumerable<int> SlowStream(
        CancellationTokenSource cts,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return 1;

        await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None).ConfigureAwait(false);
        await cts.CancelAsync();
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        yield return 2;
    }
}
