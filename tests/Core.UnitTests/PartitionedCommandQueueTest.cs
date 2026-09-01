using ActualChat.Messaging;

namespace ActualChat.Core.UnitTests;

public sealed class PartitionedCommandQueueTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void UpdateShouldReturnItemToRunOnIdleLaneAndQueueItWhileBusy()
    {
        // arrange
        var queue = new PartitionedCommandQueue<Item>();
        var i1 = new Item(1);
        var i2 = new Item(2);

        // act
        var first = queue.Update("a", p => ReplaceAllWith(p, i1));
        var second = queue.Update("a", p => ReplaceAllWith(p, i2));

        // assert
        first.Should().Be(i1, because: "the lane was idle");
        second.Should().BeNull(because: "the lane is busy running i1");
        queue.GetPending("a").Should().Equal([i2], because: "i1 is running, so only i2 waits");
    }

    [Fact]
    public void UpdateShouldCoalesceWhileRunning()
    {
        // arrange
        var queue = new PartitionedCommandQueue<Item>();
        var latest = new Item(3);

        // act
        queue.Update("a", p => ReplaceAllWith(p, new Item(1)));
        queue.Update("a", p => ReplaceAllWith(p, new Item(2)));
        queue.Update("a", p => ReplaceAllWith(p, latest));

        // assert
        queue.GetPending("a").Should().Equal([latest], because: "the newest command replaces the waiting one");
        queue.GetPendingCount("a").Should().Be(1, because: "a partition holds at most one waiting command");
    }

    [Fact]
    public void OnCompletedShouldPromoteNextThenClearRunning()
    {
        // arrange
        var queue = new PartitionedCommandQueue<Item>();
        var i1 = new Item(1);
        var i2 = new Item(2);
        var i3 = new Item(3);
        queue.Update("a", p => ReplaceAllWith(p, i1));
        queue.Update("a", p => ReplaceAllWith(p, i2));

        // act
        var promoted = queue.OnCompleted("a");
        var afterDrain = queue.OnCompleted("a");
        var restart = queue.Update("a", p => ReplaceAllWith(p, i3));

        // assert
        promoted.Should().Be(i2, because: "the waiting command runs next");
        afterDrain.Should().BeNull(because: "nothing is left, so the lane goes idle");
        restart.Should().Be(i3, because: "an idle lane runs the newcomer right away");
    }

    [Fact]
    public void DifferentPartitionsShouldBeIndependent()
    {
        // arrange
        var queue = new PartitionedCommandQueue<Item>();
        var a1 = new Item(1);
        var b1 = new Item(2);

        // act
        var a = queue.Update("a", p => ReplaceAllWith(p, a1));
        var b = queue.Update("b", p => ReplaceAllWith(p, b1));

        // assert
        a.Should().Be(a1, because: "lane 'a' was idle");
        b.Should().Be(b1, because: "a busy lane 'a' must not block lane 'b'");
    }

    [Fact]
    public void OfflineBacklogShouldCoalesceToSinglePending()
    {
        // arrange
        var queue = new PartitionedCommandQueue<Item>();
        queue.Update("a", p => ReplaceAllWith(p, new Item(1)));

        // act
        for (var v = 2; v <= 6; v++)
            queue.Update("a", p => ReplaceAllWith(p, new Item(v)));

        // assert
        queue.GetPendingCount("a").Should().Be(1, because: "the whole offline burst collapses into one command");
        queue.GetPending("a").Should().Equal([new Item(6)], because: "the latest command wins");
        queue.OnCompleted("a").Should().Be(new Item(6), because: "the reconnect drains the coalesced command");
        queue.OnCompleted("a").Should().BeNull(because: "the backlog is drained");
    }

    [Fact]
    public void ChangedShouldFireOnUpdateAndOnCompleted()
    {
        // arrange
        var queue = new PartitionedCommandQueue<Item>();
        var changeCount = 0;
        queue.Changed += () => changeCount++;

        // act
        queue.Update("a", p => ReplaceAllWith(p, new Item(1)));
        queue.Update("a", p => ReplaceAllWith(p, new Item(2)));
        queue.OnCompleted("a");

        // assert
        changeCount.Should().Be(3, because: "every queue mutation notifies its observers");
    }

    // Private methods

    private static QueueEdits<Item> ReplaceAllWith(IReadOnlyList<Item> pending, Item item)
    {
        var edits = new QueueEdits<Item>();
        foreach (var p in pending)
            edits.Remove(p);

        return edits.Add(item);
    }

    // Nested types

    private sealed record Item(int Value);
}
