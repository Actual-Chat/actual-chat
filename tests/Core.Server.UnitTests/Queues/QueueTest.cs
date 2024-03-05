namespace ActualChat.Core.Server.UnitTests.Queues;

public class QueueTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        var s = Queue.None;
        s.IsNone.Should().BeTrue();
        s.IsUndefined.Should().BeFalse();
        s.IsValid.Should().BeFalse();
        s.NullIfUndefined().Should().BeSameAs(s);

        s = Queue.Undefined;
        s.IsNone.Should().BeFalse();
        s.IsUndefined.Should().BeTrue();
        s.IsValid.Should().BeFalse();
        s.NullIfUndefined().Should().BeNull();

        s = Queue.Default;
        s.IsNone.Should().BeFalse();
        s.IsUndefined.Should().BeFalse();
        s.IsValid.Should().BeTrue();
        s.NullIfUndefined().Should().BeSameAs(s);
    }
}
