namespace ActualChat.Core.UnitTests.Collections;

public class BlockRingBufferTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void Deterministic_Wraparound_On_Push_And_Consume()
    {
        // Buffer length will be next power of two >= minCapacity+1. For 7 -> length = 8, capacity = 7
        var rb = new BlockRingBuffer<int>(7);
        rb.Capacity.Should().Be(7);
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
        rb.TryPush([6, 7, 8, 9, 10]).Should().BeTrue();
        rb.Count.Should().Be(7); // full
        rb.IsFull.Should().BeTrue();

        // Now pull 6 -> must wrap on Consume (readPos=3, 3+6>8)
        using (var block = rb.Pull(6))
            block.Memory.ToArray()
                .Should()
                .Equal(4,
                    5,
                    6,
                    7,
                    8,
                    9);
        rb.Count.Should().Be(1);
        rb.IsEmpty.Should().BeFalse();
        rb.GetAvailableContinuousData().ToArray().Should().Equal(10);

        // Finally, pull the last item
        using (var block = rb.Pull(1))
            block.Memory.Span[0].Should().Be(10);
        rb.Count.Should().Be(0);
        rb.IsEmpty.Should().BeTrue();
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
}
