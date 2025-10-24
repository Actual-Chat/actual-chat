using System.Buffers;

namespace ActualChat.Core.UnitTests.Collections;

public class BlockRingBufferTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void Deterministic_Wraparound_On_Push_And_Consume()
    {
        // Buffer length will be next power of two >= minCapacity+1. For 15 -> length = 16, capacity = 15
        var rb = new BlockRingBuffer<int>(15);
        rb.Capacity.Should().Be(15);
        rb.Count.Should().Be(0);
        rb.IsEmpty.Should().BeTrue();

        // Push 5 -> no wrap
        rb.TryPush([1, 2, 3, 4, 5]).Should().BeTrue();
        rb.Count.Should().Be(5);
        rb.IsEmpty.Should().BeFalse();
        rb.IsFull.Should().BeFalse();

        // Consumer pulls 3 (no wrap yet), Dispose commits consumption
        using (var block = rb.Pull(3))
            block.Memory.ToArray().Should().Equal(1, 2, 3);
        rb.Count.Should().Be(2);

        // Now push 5 more -> must wrap on Push (writePos=5, length=8, 5+5>8)
        rb.TryPush([6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18]).Should().BeTrue();
        rb.Count.Should().Be(15); // full
        rb.IsFull.Should().BeTrue();

        // Now pull 6 -> must wrap on Consume (readPos=3, 3+6>8)
        using (var block = rb.Pull(6))
            block.Memory.ToArray()
                .Should()
                .Equal(4, 5, 6, 7, 8, 9);
        rb.Count.Should().Be(9);
        rb.IsEmpty.Should().BeFalse();
        rb.GetAvailableContinuousData().ToArray().Should().Equal([10, 11, 12, 13, 14, 15, 16]);

        // Finally, pull the last item
        using (var block = rb.Pull(1))
            block.Memory.Span[0].Should().Be(10);
        rb.Count.Should().Be(8);
        using (var block = rb.Pull(8))
            block.Memory.Span.ToArray().Should().Equal([11, 12, 13, 14, 15, 16, 17, 18]);
        rb.IsEmpty.Should().BeTrue();

        rb.TryPull(1, out var emptyBlock).Should().BeFalse();
        emptyBlock.Should().BeNull();
    }

    [Fact]
    public async Task SPSC_Producer_Consumer_With_Wraparounds()
    {
        var capacity = 15; // underlying length 16, capacity 15
        var rb = new BlockRingBuffer<int>(capacity);

        var total = 1000;
        var produced = 0;
        var consumed = 0;
        var received = new List<int>(total);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = cts.Token;

        // Producer pushes variable-size batches causing occasional wrap
        var producer = Task.Run(async () => {
                var rnd = new Random(123);
                while (produced < total && !token.IsCancellationRequested) {
                    var remaining = total - produced;
                    var batch = Math.Min(1 + rnd.Next(0, 6), remaining); // 1..6

                    // Out.WriteLine("rb: pushing {0}..{1}", produced, produced + batch);

                    // keep trying until space becomes available
                    while (!rb.TryPush(Enumerable.Range(produced, batch).ToArray()))
                        await Task.Delay(0, token); // yield
                    produced += batch;
                }
            },
            token);

        // Consumer pulls variable-size batches, ensuring correct order
        var consumer = Task.Run(async () => {
                var rnd = new Random(321);
                while (consumed < total && !token.IsCancellationRequested) {
                    // Out.WriteLine("rb: count={0}, remaining={1}", rb.Count, total - consumed);
                    var toRead = 1 + rnd.Next(0, 8); // 1..8
                    // Try pull; if insufficient, wait a bit
                    if (!rb.TryPull(Math.Min(toRead, rb.Count), out var block)) {
                        await Task.Delay(0, token);
                        continue;
                    }
                    using var _ = block;
                    foreach (var v in block.Memory.Span) {
                        received.Add(v);
                        consumed++;
                    }
                }
            },
            token);

        await Task.WhenAll(producer, consumer);

        consumed.Should().Be(total, "consumer must receive all items");
        received.Count.Should().Be(total);
        received.Should().Equal(Enumerable.Range(0, total));

        rb.IsEmpty.Should().BeTrue();
        rb.Count.Should().Be(0);
    }

    [Fact]
    public void Multiple_Pulls_With_Delayed_Dispose_Should_Delay_Commit()
    {
        // Use a small requested capacity; actual capacity may be larger due to MemoryPool rounding
        var rb = new BlockRingBuffer<int>(7);
        rb.Count.Should().Be(0);
        rb.IsEmpty.Should().BeTrue();

        var cap = rb.Capacity;

        // Choose k >= 1 so that (cap - k) + (k + 1) > cap, guaranteeing initial push of (k+1) fails
        var k = Math.Max(1, cap / 4); // keep some headroom
        var initialFill = Math.Max(2, cap - k);

        // Prepare initial data 1..initialFill
        var initialData = Enumerable.Range(1, initialFill).ToArray();
        rb.TryPush(initialData).Should().BeTrue();
        rb.Count.Should().Be(initialFill);

        // Pull twice without disposing immediately -> commits must be delayed
        var p1 = Math.Max(1, Math.Min(2, initialFill - 1)); // at least 1
        var p2 = 1;
        var b1 = rb.Pull(p1);
        var b2 = rb.Pull(p2);

        // Count hasn't changed yet because commits happen on Dispose
        rb.Count.Should().Be(initialFill);

        var pushLen = k + 1; // exceeds capacity when nothing is committed
        var pushData = Enumerable.Range(initialFill + 1, pushLen).ToArray();

        // Not enough committed space for pushing yet
        rb.TryPush(pushData).Should().BeFalse();

        // Dispose first block -> commit p1 items
        b1.Dispose();
        rb.Count.Should().Be(initialFill - p1);

        // Now there is enough space for pushLen, push succeeds
        rb.TryPush(pushData).Should().BeTrue();
        rb.Count.Should().Be(initialFill - p1 + pushLen);

        // Disposing second block commits p2 more
        b2.Dispose();
        rb.Count.Should().Be(initialFill - (p1 + p2) + pushLen);

        // Pull the remaining items and verify ordering
        var remaining = rb.Count;
        using (var b3 = rb.Pull(remaining)) {
            var expected = Enumerable.Range(p1 + p2 + 1, initialFill - (p1 + p2))
                .Concat(Enumerable.Range(initialFill + 1, pushLen))
                .ToArray();
            b3.Memory.ToArray().Should().Equal(expected);
        }

        rb.IsEmpty.Should().BeTrue();
        rb.Count.Should().Be(0);
    }

    [Fact]
    public void NoSpaceAndNoDataShouldBeImpossibleWhenNoOutstandingBlocks()
    {
        // Use a modest capacity to exercise wraparounds frequently
        var rb = new BlockRingBuffer<int>(15);
        var rng = new Random(42);
        var nextValue = 1;

        // We'll occasionally hold onto blocks, but we assert the invariant only when none are held
        var held = new List<IMemoryOwner<int>>();
        var one = new int[1];

        for (var i = 0; i < 20_000; i++)
        {
            // Randomly dispose some held blocks
            if (held.Count > 0 && rng.NextDouble() < 0.3)
            {
                var idx = rng.Next(held.Count);
                held[idx].Dispose();
                held.RemoveAt(idx);
            }

            if (held.Count == 0)
            {
                one[0] = nextValue;
                var canPush = rb.TryPush(one);
                if (canPush) nextValue++;

                var canPull = rb.TryPull(1, out var block);
                if (canPull)
                {
                    // We don't assert ordering here; this test targets the impossible state invariant
                    block!.Dispose();
                }

                // Invariant: when there are no outstanding blocks, it must be impossible
                // that producer and consumer are both blocked simultaneously.
                if (!canPush && !canPull)
                {
                    // Helpful diagnostics
                    var count = rb.Count;
                    var cap = rb.Capacity;
                    var state = $"Iteration={i}, Count={count}, Capacity={cap}";
                    throw new Xunit.Sdk.XunitException($"Invalid state: both TryPush and TryPull failed with no outstanding blocks. {state}");
                }
                continue;
            }

            // With some probability, try to pull and hold a small block
            if (rng.NextDouble() < 0.5)
            {
                if (rb.TryPull(1, out var block))
                {
                    // Keep the block for a while to emulate delayed commit
                    held.Add(block!);
                }
            }
            else
            {
                // Try pushing a single item
                one[0] = nextValue;
                if (rb.TryPush(one))
                    nextValue++;
            }
        }

        // Clean up
        foreach (var b in held)
            b.Dispose();

        // Drain the buffer to ensure consistency
        while (rb.TryPull(Math.Min(4, rb.Count), out var block))
            block!.Dispose();

        rb.IsEmpty.Should().BeTrue();
        rb.Count.Should().Be(0);
    }
}
