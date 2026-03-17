namespace ActualChat.Core.UnitTests.Collections;

public class BlockRingBufferTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicWriteAndRead()
    {
        var rb = new BlockRingBuffer<int>(10);
        rb.Capacity.Should().BeGreaterThanOrEqualTo(10);

        rb.TryWrite([1, 2, 3, 4, 5]).Should().BeTrue();
        rb.Count.Should().Be(5);

        rb.TryRead(5, out var data, out _).Should().BeTrue();
        data.ToArray().Should().Equal(1, 2, 3, 4, 5);
        rb.Count.Should().Be(0);
    }

    [Fact]
    public void PartialReadAndWrite()
    {
        var rb = new BlockRingBuffer<int>(10);
        rb.TryWrite([0, 1, 2, 3, 4, 5, 6, 7]).Should().BeTrue();

        rb.TryRead(3, out var data, out _).Should().BeTrue();
        data.ToArray().Should().Equal(0, 1, 2);
        rb.Count.Should().Be(5);

        rb.TryRead(5, out data, out _).Should().BeTrue();
        data.ToArray().Should().Equal(3, 4, 5, 6, 7);
        rb.Count.Should().Be(0);
    }

    [Fact]
    public void CapacityBoundaryWrap()
    {
        var rb = new BlockRingBuffer<int>(10);
        var cap = rb.Capacity;

        var fill = new int[cap - 2];
        for (var i = 0; i < fill.Length; i++) fill[i] = i;
        rb.TryWrite(fill).Should().BeTrue();

        rb.TryRead(cap, out _, out _).Should().BeTrue();

        rb.TryWrite([100, 101, 102, 103, 104]).Should().BeTrue();

        var allRead = new List<int>();
        while (rb.Count > 0) {
            rb.TryRead(rb.Count, out var chunk, out _).Should().BeTrue();
            chunk.Length.Should().BeGreaterThan(0);
            foreach (var v in chunk.Span)
                allRead.Add(v);
        }
        allRead.Should().Equal(100, 101, 102, 103, 104);
    }

    [Fact]
    public void MultipleWrapCycles()
    {
        var rb = new BlockRingBuffer<int>(8);
        var cap = rb.Capacity;
        var nextVal = 0;
        var readVal = 0;

        for (var cycle = 0; cycle < 20; cycle++) {
            var toWrite = cap - 1;
            var items = new int[toWrite];
            for (var i = 0; i < toWrite; i++) items[i] = nextVal++;
            rb.TryWrite(items).Should().BeTrue($"cycle {cycle} write");

            while (rb.Count > 0) {
                rb.TryRead(rb.Count, out var rd, out _).Should().BeTrue();
                foreach (var v in rd.Span) {
                    v.Should().Be(readVal, $"cycle {cycle}, expected {readVal}");
                    readVal++;
                }
            }
        }
    }

    [Fact]
    public void FullBufferAndDrain()
    {
        var rb = new BlockRingBuffer<int>(8);
        var cap = rb.Capacity;

        var items = new int[cap];
        for (var i = 0; i < cap; i++) items[i] = i;
        rb.TryWrite(items).Should().BeTrue();
        rb.Count.Should().Be(cap);

        // Next write should fail (partial = 0)
        rb.TryWrite([999]).Should().BeFalse();

        var drained = new List<int>();
        while (rb.Count > 0) {
            rb.TryRead(cap, out var rd, out _).Should().BeTrue();
            foreach (var v in rd.Span)
                drained.Add(v);
        }
        drained.Should().Equal(Enumerable.Range(0, cap));
    }

    [Fact]
    public void PartialWrite()
    {
        var rb = new BlockRingBuffer<int>(4);
        var cap = rb.Capacity;

        // Fill half
        rb.TryWrite(new int[cap / 2]).Should().BeTrue();

        // Try to write more than remaining — partial write
        var big = new int[cap];
        rb.TryWrite(big, out var written).Should().BeFalse();
        written.Should().Be(cap - cap / 2);
    }

    [Fact]
    public async Task WhenWrittenSignaling()
    {
        var rb = new BlockRingBuffer<int>(10);

        // Empty buffer — TryRead returns false with a pending task
        rb.TryRead(1, out var data, out var whenReady).Should().BeFalse();
        whenReady.Should().NotBeNull();
        data.IsEmpty.Should().BeTrue();

        // Write triggers it
        rb.TryWrite([42]).Should().BeTrue();
        await whenReady!.WaitAsync(TimeSpan.FromSeconds(1));

        // Now read succeeds
        rb.TryRead(1, out data, out _).Should().BeTrue();
        data.Span[0].Should().Be(42);
    }

    [Fact]
    public async Task ConcurrentProducerConsumer()
    {
        var rb = new BlockRingBuffer<int>(15);
        var total = 5000;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = cts.Token;

        var produced = 0;
        var producer = Task.Run(() => {
            var rng = new Random(123);
            while (produced < total) {
                ct.ThrowIfCancellationRequested();
                var batch = Math.Min(1 + rng.Next(6), total - produced);
                var items = new int[batch];
                for (var i = 0; i < batch; i++) items[i] = produced + i;
                var remaining = items.AsSpan();
                while (remaining.Length > 0) {
                    rb.TryWrite(remaining, out var written);
                    produced += written;
                    remaining = remaining[written..];
                    if (remaining.Length > 0)
                        Thread.Sleep(1);
                }
            }
        }, ct);

        var received = new List<int>(total);
        var consumer = Task.Run(async () => {
            var rng = new Random(321);
            while (received.Count < total) {
                ct.ThrowIfCancellationRequested();
                if (!rb.TryRead(1 + rng.Next(8), out var rd, out var whenReady)) {
                    await whenReady.WaitAsync(ct).ConfigureAwait(false);
                    continue;
                }
                foreach (var v in rd.Span)
                    received.Add(v);
            }
        }, ct);

        await Task.WhenAll(producer, consumer);
        received.Count.Should().Be(total);
        received.Should().Equal(Enumerable.Range(0, total));
    }

    [Fact]
    public void ClearResetsState()
    {
        var rb = new BlockRingBuffer<int>(10);
        rb.TryWrite([1, 2]).Should().BeTrue();
        rb.Count.Should().Be(2);

        rb.Clear();
        rb.Count.Should().Be(0);
        rb.TryRead(1, out var data, out _).Should().BeFalse(); // empty
        data.IsEmpty.Should().BeTrue();
        rb.TryWrite([3]).Should().BeTrue(); // writable
    }

    [Fact]
    public void FullBufferRejectsWrite()
    {
        var rb = new BlockRingBuffer<int>(4);
        var cap = rb.Capacity;

        rb.TryWrite(new int[cap]).Should().BeTrue();
        rb.TryWrite([1]).Should().BeFalse();
    }

    [Fact]
    public void ReadMoreThanAvailableReturnsAvailable()
    {
        var rb = new BlockRingBuffer<int>(4);
        rb.TryWrite([1, 2]).Should().BeTrue();

        rb.TryRead(10, out var data, out _).Should().BeTrue();
        data.Length.Should().Be(2);
    }

    [Fact]
    public void CapacityDerivedFromRentedBuffer()
    {
        var rb = new BlockRingBuffer<int>(10);
        rb.Capacity.Should().BeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public async Task ProducerConsumerWithWrapStress()
    {
        // Use a larger buffer to avoid the SPSC zero-copy race
        // (TryRead returns a view into the buffer that the producer can overwrite)
        var rb = new BlockRingBuffer<int>(64);
        var total = 10_000;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = cts.Token;

        var producer = Task.Run(() => {
            for (var produced = 0; produced < total;) {
                ct.ThrowIfCancellationRequested();
                // Write one item at a time for simplicity
                Span<int> item = [produced];
                if (rb.TryWrite(item))
                    produced++;
                else
                    Thread.Sleep(1);
            }
        }, ct);

        var received = new List<int>(total);
        var consumer = Task.Run(async () => {
            while (received.Count < total) {
                ct.ThrowIfCancellationRequested();
                if (!rb.TryRead(16, out var rd, out var whenReady)) {
                    await whenReady.WaitAsync(ct).ConfigureAwait(false);
                    continue;
                }
                // Copy immediately before producer overwrites
                foreach (var v in rd.Span)
                    received.Add(v);
            }
        }, ct);

        await Task.WhenAll(producer, consumer);
        received.Should().Equal(Enumerable.Range(0, total));
    }

    [Fact]
    public void ReadSplitsAtWrapGap()
    {
        var rb = new BlockRingBuffer<int>(8);
        var cap = rb.Capacity;

        var fill = new int[cap - 1];
        for (var i = 0; i < fill.Length; i++) fill[i] = i;
        rb.TryWrite(fill).Should().BeTrue();

        rb.TryRead(fill.Length - 2, out _, out _).Should().BeTrue();
        rb.Count.Should().Be(2);

        rb.TryWrite([100, 101, 102]).Should().BeTrue();

        var allRead = new List<int>();
        while (rb.Count > 0) {
            rb.TryRead(rb.Count, out var rd, out _).Should().BeTrue();
            rd.Length.Should().BeGreaterThan(0);
            foreach (var v in rd.Span)
                allRead.Add(v);
        }

        var expected = Enumerable.Range(fill.Length - 2, 2).Concat([100, 101, 102]).ToArray();
        allRead.Should().Equal(expected);
    }

    [Fact]
    public void NoSpaceAndNoDataShouldBeImpossible()
    {
        var rb = new BlockRingBuffer<int>(15);
        var cap = rb.Capacity;
        var nextValue = 1;

        for (var i = 0; i < 20_000; i++) {
            var canWrite = rb.TryWrite([nextValue]);
            if (canWrite) nextValue++;

            var canRead = rb.TryRead(1, out _, out _);

            if (!canWrite && !canRead)
                throw new Xunit.Sdk.XunitException(
                    $"Invalid state at iteration {i}: both TryWrite and TryRead failed. Count={rb.Count}, Capacity={cap}");
        }
    }

    [Fact]
    public async Task ReadAllExtension()
    {
        var rb = new BlockRingBuffer<int>(128);
        var total = 100;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ct = cts.Token;

        var producer = Task.Run(() => {
            var produced = 0;
            while (produced < total) {
                ct.ThrowIfCancellationRequested();
                var batch = Math.Min(total - produced, 7);
                var items = new int[batch];
                for (var i = 0; i < batch; i++) items[i] = produced + i;
                var remaining = items.AsSpan();
                while (remaining.Length > 0) {
                    rb.TryWrite(remaining, out var written);
                    produced += written;
                    remaining = remaining[written..];
                    if (remaining.Length > 0)
                        Thread.Sleep(1);
                }
            }
        }, ct);

        var received = new List<int>();
        await foreach (var chunk in rb.ReadAll(7, ct)) {
            foreach (var v in chunk.Span)
                received.Add(v);
            if (received.Count >= total)
                break;
        }

        await producer;
        received.Count.Should().BeGreaterThanOrEqualTo(total);
        received.Take(total).Should().Equal(Enumerable.Range(0, total));
    }

    [Fact]
    public async Task DisposeCancelsWhenReadyToRead()
    {
        var rb = new BlockRingBuffer<int>(4);

        rb.TryRead(1, out _, out var whenReady).Should().BeFalse();
        whenReady.Should().NotBeNull();

        rb.Dispose();

        var act = async () => await whenReady!;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
