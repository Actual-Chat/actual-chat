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

        var buf = new int[5];
        rb.TryRead(buf, out _).Should().BeTrue();
        buf.Should().Equal(1, 2, 3, 4, 5);
        rb.Count.Should().Be(0);
    }

    [Fact]
    public void PartialReadAndWrite()
    {
        var rb = new BlockRingBuffer<int>(10);
        rb.TryWrite([0, 1, 2, 3, 4, 5, 6, 7]).Should().BeTrue();

        var buf = new int[3];
        rb.TryRead(buf, out _).Should().BeTrue();
        buf.Should().Equal(0, 1, 2);
        rb.Count.Should().Be(5);

        var buf2 = new int[5];
        rb.TryRead(buf2, out _).Should().BeTrue();
        buf2.Should().Equal(3, 4, 5, 6, 7);
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

        var discard = new int[cap - 2];
        rb.TryRead(discard, out _).Should().BeTrue();

        rb.TryWrite([100, 101, 102, 103, 104]).Should().BeTrue();

        var readBuf = new int[5];
        rb.TryRead(readBuf, out _).Should().BeTrue();
        readBuf.Should().Equal(100, 101, 102, 103, 104);
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
                var count = rb.Count;
                var buf = new int[count];
                rb.TryRead(buf, out _).Should().BeTrue();
                for (var i = 0; i < count; i++) {
                    buf[i].Should().Be(readVal, $"cycle {cycle}, expected {readVal}");
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

        var buf = new int[cap];
        rb.TryRead(buf, out _).Should().BeTrue();
        buf.Should().Equal(Enumerable.Range(0, cap));
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
        var buf = new int[1];
        rb.TryRead(buf, out var whenReady).Should().BeFalse();
        whenReady.Should().NotBeNull();

        // Write triggers it
        rb.TryWrite([42]).Should().BeTrue();
        await whenReady!.WaitAsync(TimeSpan.FromSeconds(1));

        // Now read succeeds
        rb.TryRead(buf, out _).Should().BeTrue();
        buf[0].Should().Be(42);
    }

    [Fact]
    public async Task WhenReadyToWriteSignaling()
    {
        var rb = new BlockRingBuffer<int>(4);
        var cap = rb.Capacity;

        // Fill the buffer
        rb.TryWrite(new int[cap]).Should().BeTrue();
        rb.IsFull.Should().BeTrue();

        // Write should fail and provide a task
        rb.TryWrite([1], out var written, out var whenReady).Should().BeFalse();
        written.Should().Be(0);
        whenReady.Should().NotBeNull();

        // Read frees space — triggers the task
        var buf = new int[1];
        rb.TryRead(buf, out _).Should().BeTrue();
        await whenReady!.WaitAsync(TimeSpan.FromSeconds(1));

        // Now write succeeds
        rb.TryWrite([99]).Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentProducerConsumer()
    {
        var rb = new BlockRingBuffer<int>(15);
        var total = 5000;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = cts.Token;

        var produced = 0;
        var producer = Task.Run(async () => {
            var rng = new Random(123);
            while (produced < total) {
                ct.ThrowIfCancellationRequested();
                var batch = Math.Min(1 + rng.Next(6), total - produced);
                var items = new int[batch];
                for (var i = 0; i < batch; i++) items[i] = produced + i;
                var remaining = items.AsMemory();
                while (remaining.Length > 0) {
                    if (rb.TryWrite(remaining.Span, out var writtenCount, out var whenReady)) {
                        produced += writtenCount;
                        break;
                    }
                    produced += writtenCount;
                    remaining = remaining[writtenCount..];
                    await whenReady!.WaitAsync(ct).ConfigureAwait(false);
                }
            }
        }, ct);

        var received = new List<int>(total);
        var consumer = Task.Run(async () => {
            var rng = new Random(321);
            while (received.Count < total) {
                ct.ThrowIfCancellationRequested();
                var wantedSize = 1 + rng.Next(8);
                var available = Math.Min(rb.Count, wantedSize);
                if (available == 0) {
                    var probe = new int[1];
                    if (!rb.TryRead(probe, out var whenReady)) {
                        await whenReady.WaitAsync(ct).ConfigureAwait(false);
                        continue;
                    }
                    received.Add(probe[0]);
                    continue;
                }
                var buf = new int[available];
                if (!rb.TryRead(buf, out _)) {
                    // Count changed between check and read, retry
                    continue;
                }
                for (var i = 0; i < available; i++)
                    received.Add(buf[i]);
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
        var buf = new int[1];
        rb.TryRead(buf, out _).Should().BeFalse(); // empty
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
    public void ReadMoreThanAvailableReturnsFalse()
    {
        var rb = new BlockRingBuffer<int>(4);
        rb.TryWrite([1, 2]).Should().BeTrue();

        // Requesting 10 items but only 2 available — returns false, data stays in buffer
        var buf = new int[10];
        rb.TryRead(buf, out var whenReady).Should().BeFalse();
        whenReady.Should().NotBeNull();
        rb.Count.Should().Be(2);

        // Read exactly what's available — succeeds
        var buf2 = new int[2];
        rb.TryRead(buf2, out _).Should().BeTrue();
        buf2.Should().Equal(1, 2);
    }

    [Fact]
    public void PartialDataStaysInBufferOnFailure()
    {
        var rb = new BlockRingBuffer<int>(10);
        rb.TryWrite([1, 2, 3]).Should().BeTrue();
        rb.Count.Should().Be(3);

        // Try to read 5 items — only 3 available, should fail
        var buf = new int[5];
        rb.TryRead(buf, out _).Should().BeFalse();

        // Count should be unchanged
        rb.Count.Should().Be(3);

        // Data should still be readable
        var buf2 = new int[3];
        rb.TryRead(buf2, out _).Should().BeTrue();
        buf2.Should().Equal(1, 2, 3);
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
        var rb = new BlockRingBuffer<int>(64);
        var total = 10_000;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = cts.Token;

        var producer = Task.Run(async () => {
            for (var produced = 0; produced < total;) {
                ct.ThrowIfCancellationRequested();
                Span<int> item = [produced];
                if (rb.TryWrite(item, out _, out var whenReady))
                    produced++;
                else
                    await whenReady!.WaitAsync(ct).ConfigureAwait(false);
            }
        }, ct);

        var received = new List<int>(total);
        var consumer = Task.Run(async () => {
            while (received.Count < total) {
                ct.ThrowIfCancellationRequested();
                var buf = new int[1];
                if (!rb.TryRead(buf, out var whenReady)) {
                    await whenReady.WaitAsync(ct).ConfigureAwait(false);
                    continue;
                }
                received.Add(buf[0]);
            }
        }, ct);

        await Task.WhenAll(producer, consumer);
        received.Should().Equal(Enumerable.Range(0, total));
    }

    [Fact]
    public void ReadAcrossWrapGap()
    {
        var rb = new BlockRingBuffer<int>(8);
        var cap = rb.Capacity;

        // Fill near capacity, then drain most, write across boundary
        var fill = new int[cap - 1];
        for (var i = 0; i < fill.Length; i++) fill[i] = i;
        rb.TryWrite(fill).Should().BeTrue();

        var discard = new int[fill.Length - 2];
        rb.TryRead(discard, out _).Should().BeTrue();
        rb.Count.Should().Be(2);

        rb.TryWrite([100, 101, 102]).Should().BeTrue();

        // New TryRead reads across the wrap gap in a single call
        var buf = new int[5];
        rb.TryRead(buf, out _).Should().BeTrue();
        buf.Should().Equal(fill[^2], fill[^1], 100, 101, 102);
    }

    [Fact]
    public void NoSpaceAndNoDataShouldBeImpossible()
    {
        var rb = new BlockRingBuffer<int>(15);
        var cap = rb.Capacity;
        var nextValue = 1;
        var discardBuf = new int[1];

        for (var i = 0; i < 20_000; i++) {
            var canWrite = rb.TryWrite([nextValue]);
            if (canWrite) nextValue++;

            var canRead = rb.TryRead(discardBuf, out _);

            if (!canWrite && !canRead)
                throw new Xunit.Sdk.XunitException(
                    $"Invalid state at iteration {i}: both TryWrite and TryRead failed. Count={rb.Count}, Capacity={cap}");
        }
    }


    [Fact]
    public async Task DisposeCancelsWhenReadyToRead()
    {
        var rb = new BlockRingBuffer<int>(4);

        var buf = new int[1];
        rb.TryRead(buf, out var whenReady).Should().BeFalse();
        whenReady.Should().NotBeNull();

        rb.Dispose();

        var act = async () => await whenReady!;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DisposeCancelsWhenReadyToWrite()
    {
        var rb = new BlockRingBuffer<int>(4);
        var cap = rb.Capacity;

        rb.TryWrite(new int[cap]).Should().BeTrue();
        rb.TryWrite([1], out _, out var whenReady).Should().BeFalse();
        whenReady.Should().NotBeNull();

        rb.Dispose();

        var act = async () => await whenReady!;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ConvenienceProperties()
    {
        var rb = new BlockRingBuffer<int>(8);
        var cap = rb.Capacity;

        rb.IsEmpty.Should().BeTrue();
        rb.IsFull.Should().BeFalse();
        rb.RemainingCapacity.Should().Be(cap);

        rb.TryWrite(new int[cap]).Should().BeTrue();
        rb.IsEmpty.Should().BeFalse();
        rb.IsFull.Should().BeTrue();
        rb.RemainingCapacity.Should().Be(0);

        var buf = new int[cap];
        rb.TryRead(buf, out _).Should().BeTrue();
        rb.IsEmpty.Should().BeTrue();
        rb.RemainingCapacity.Should().Be(cap);
    }

    [Fact]
    public async Task WriteExtension()
    {
        var rb = new BlockRingBuffer<int>(4);
        var cap = rb.Capacity;

        // Write more than capacity — should block until consumer frees space
        var data = new int[cap + 2];
        for (var i = 0; i < data.Length; i++) data[i] = i;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ct = cts.Token;

        var writer = Task.Run(async () => {
            await rb.Write(data.AsMemory(), ct).ConfigureAwait(false);
        }, ct);

        // Wait for buffer to fill
        while (rb.Count < cap && !ct.IsCancellationRequested)
            await Task.Delay(10, ct);

        // Read some to unblock writer
        var buf = new int[cap + 2];
        var totalRead = 0;
        while (totalRead < data.Length) {
            var remaining = data.Length - totalRead;
            var available = Math.Min(rb.Count, remaining);
            if (available == 0) {
                var probe = new int[1];
                if (!rb.TryRead(probe, out var whenReady)) {
                    await whenReady.WaitAsync(ct).ConfigureAwait(false);
                    continue;
                }
                buf[totalRead] = probe[0];
                totalRead += 1;
                continue;
            }
            var readBuf = new int[available];
            if (!rb.TryRead(readBuf, out _))
                continue;
            readBuf.CopyTo(buf.AsSpan(totalRead));
            totalRead += available;
        }

        await writer;
        buf[..data.Length].Should().Equal(data);
    }

    [Fact]
    public async Task WhenReadyToReadNonConsuming()
    {
        var rb = new BlockRingBuffer<int>(10);

        // Empty buffer — returns a pending task
        var whenReady = rb.WhenReadyToRead();
        whenReady.Should().NotBeNull();

        // Write triggers it
        rb.TryWrite([42]).Should().BeTrue();
        await whenReady!.WaitAsync(TimeSpan.FromSeconds(1));

        // Data still in buffer (not consumed)
        rb.Count.Should().Be(1);

        // Non-empty buffer — returns null (ready now)
        rb.WhenReadyToRead().Should().BeNull();
    }

    [Fact]
    public async Task ClearSignalsWhenReadyToWrite()
    {
        var rb = new BlockRingBuffer<int>(4);
        var cap = rb.Capacity;

        rb.TryWrite(new int[cap]).Should().BeTrue();
        rb.TryWrite([1], out _, out var whenReady).Should().BeFalse();
        whenReady.Should().NotBeNull();

        rb.Clear();
        await whenReady!.WaitAsync(TimeSpan.FromSeconds(1));

        // Now write succeeds
        rb.TryWrite([1]).Should().BeTrue();
    }
}
