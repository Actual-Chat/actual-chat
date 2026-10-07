using ActualChat.Flows;
using ActualLab.Versioning;

namespace ActualChat.Core.Server.UnitTests.Flows;

public sealed class FlowInboxTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly VersionGenerator<long> IdGenerator
        = new ClockBasedVersionGenerator(MomentClockSet.Default.SystemClock);

    [Fact]
    public void InboxShouldNotCopyStoredMessagesUntilChanged()
    {
        // arrange
        ApiArray<FlowInboxMessage> stored = [Stored(1, "a"), Stored(2, "b")];
        var inbox = new FlowInbox(stored);

        // act, assert
        inbox.Messages.Should().BeSameAs(stored.Items);
        inbox.GetDiff().Should().BeSameAs(FlowInboxDiff.Empty);
        inbox.Remove(1);
        inbox.Messages.Should().NotBeSameAs(stored.Items);
        stored.Select(m => m.Id).Should().Equal(1, 2);
    }

    [Fact]
    public void ApplyingNothingShouldReturnTheSameMessages()
    {
        // arrange
        ApiArray<FlowInboxMessage> stored = [Stored(1, "a")];
        var lastId = 1L;

        // act
        var result = new FlowInboxDiff([], [42]).ApplyTo(stored, ref lastId, IdGenerator);

        // assert
        result.Items.Should().BeSameAs(stored.Items);
        lastId.Should().Be(1);
    }

    [Fact]
    public void AddedMessagesShouldFollowStoredOnesUntilStored()
    {
        // arrange
        var stored = Stored(5, "stored");
        var inbox = new FlowInbox([stored]);

        // act
        var first = inbox.Add(new FlowInboxTestMessage("first"));
        var second = inbox.Add(new FlowInboxTestMessage("second"));

        // assert
        inbox.Messages.Should().Equal(stored, first, second);
        first.IsStored.Should().BeFalse();
        inbox.GetDiff().Added.Should().Equal(first, second);
    }

    [Fact]
    public void RemovingAMessageAddedInTheSameResumeShouldLeaveNoTrace()
    {
        // arrange
        var stored = Stored(1, "stored");
        var inbox = new FlowInbox([stored]);
        var added = inbox.Add(new FlowInboxTestMessage("added"));

        // act
        var isRemoved = inbox.Remove(added);

        // assert
        isRemoved.Should().BeTrue();
        inbox.HasChanges.Should().BeFalse();
        inbox.GetDiff().Should().BeSameAs(FlowInboxDiff.Empty);
        inbox.Messages.Should().Equal(stored);
    }

    [Fact]
    public void RemovingAnUnknownMessageShouldChangeNothing()
    {
        // arrange
        var inbox = new FlowInbox([Stored(1, "a")]);

        // act
        var isRemoved = inbox.Remove(2);

        // assert
        isRemoved.Should().BeFalse();
        inbox.HasChanges.Should().BeFalse();
        inbox.Count.Should().Be(1);
    }

    [Fact]
    public void ClearShouldRemoveStoredMessagesAndForgetAddedOnes()
    {
        // arrange
        var stored = Stored(1, "stored");
        var inbox = new FlowInbox([stored]);
        inbox.Add(new FlowInboxTestMessage("added"));

        // act
        inbox.Clear();

        // assert
        inbox.Count.Should().Be(0);
        var diff = inbox.GetDiff();
        diff.Added.Should().BeEmpty();
        diff.RemovedIds.Should().Equal(1);
    }

    [Fact]
    public void AcceptChangesShouldStartANewDiff()
    {
        // arrange
        var (a, b) = (Stored(1, "a"), Stored(2, "b"));
        var inbox = new FlowInbox([a, b]);
        inbox.Remove(a);
        inbox.Add(new FlowInboxTestMessage("added"));

        // act
        inbox.AcceptChanges();

        // assert
        inbox.HasChanges.Should().BeFalse();
        inbox.Messages.Should().Equal(b);
        inbox.Remove(b).Should().BeTrue();
        inbox.GetDiff().RemovedIds.Should().Equal(2);
    }

    [Fact]
    public void DiffShouldKeepMessagesPostedAfterTheInboxWasLoaded()
    {
        // arrange
        var (processed, kept) = (Stored(1, "processed"), Stored(2, "kept"));
        var inbox = new FlowInbox([processed, kept]);
        inbox.Remove(processed);
        inbox.Add(new FlowInboxTestMessage("added"));
        var posted = Stored(3, "posted meanwhile");
        var lastId = 3L;

        // act
        var result = inbox.GetDiff().ApplyTo([processed, kept, posted], ref lastId, IdGenerator);

        // assert
        result.Select(Text).Should().Equal("kept", "posted meanwhile", "added");
        result[^1].Id.Should().Be(lastId).And.BeGreaterThan(3);
    }

    [Fact]
    public void AppliedAdditionsShouldGetGrowingIdsInTheirOrder()
    {
        // arrange
        var diff = new FlowInboxDiff(
            [FlowInboxMessage.New(new FlowInboxTestMessage("a")), FlowInboxMessage.New(new FlowInboxTestMessage("b"))],
            []);
        var lastId = long.MaxValue / 2;

        // act
        var result = diff.ApplyTo([], ref lastId, IdGenerator);

        // assert
        result.Select(Text).Should().Equal("a", "b");
        result[0].Id.Should().BeGreaterThan(long.MaxValue / 2);
        result[1].Id.Should().Be(result[0].Id + 1).And.Be(lastId);
    }

    [Fact]
    public void UnreadablePayloadShouldBeNull()
    {
        // arrange
        var message = new FlowInboxMessage(1, [0xC1, 0x00, 0xFF]);

        // act, assert
        message.Payload.Should().BeNull();
    }

    [Fact]
    public void WithShouldNotCarryAStalePayload()
    {
        // arrange
        var a = FlowInboxMessage.New(new FlowInboxTestMessage("a"));
        var b = FlowInboxMessage.New(new FlowInboxTestMessage("b"));
        _ = a.Payload;

        // act
        var copy = a with { Data = b.Data };

        // assert
        copy.Payload.Should().Be(new FlowInboxTestMessage("b"));
        copy.Should().Be(new FlowInboxMessage(0, b.Data.ToArray()));
    }

    [Fact]
    public void MessageShouldRestoreItsPayloadAfterAnySerializer()
    {
        // arrange
        var message = Stored(42, "payload");

        // act, assert
        message.AssertPassesThroughSerializers(m => {
            m.Id.Should().Be(42);
            m.Payload.Should().Be(new FlowInboxTestMessage("payload"));
        }, Out);
    }

    [Fact]
    public void ChangeInboxCommandShouldPassThroughSerializers()
    {
        // arrange
        var flowId = new FlowId("SomeFlow:args");
        var post = Flows_ChangeInbox.Post(flowId, new FlowInboxTestMessage("a"), new FlowInboxTestMessage("b"));
        var remove = Flows_ChangeInbox.Remove(flowId, 1, 2);

        // act, assert
        post.MustResume.Should().BeTrue();
        post.AssertPassesThroughSerializers(c => {
            c.FlowId.Should().Be(flowId);
            c.MustResume.Should().BeTrue();
            c.Diff.Added.Select(m => m.Payload).Should().Equal(
                new FlowInboxTestMessage("a"), new FlowInboxTestMessage("b"));
        }, Out);
        remove.MustResume.Should().BeFalse();
        remove.AssertPassesThroughSerializers(c => c.Diff.RemovedIds.Should().Equal(1, 2), Out);
    }

    // Private methods

    private static FlowInboxMessage Stored(long id, string text)
        => FlowInboxMessage.New(new FlowInboxTestMessage(text)) with { Id = id };

    private static string Text(FlowInboxMessage message)
        => ((FlowInboxTestMessage)message.Payload!).Text;
}

[DataContract, MessagePackObject]
public sealed partial record FlowInboxTestMessage(
    [property: DataMember, Key(0)] string Text);
