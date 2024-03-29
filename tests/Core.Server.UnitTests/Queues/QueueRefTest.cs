using ActualChat.Queues;

namespace ActualChat.Core.Server.UnitTests.Queues;

public class QueueRefTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void BasicTest()
    {
        var s = QueueRef.None;
        s.IsNone.Should().BeTrue();
        s.IsUndefined.Should().BeFalse();
        s.IsValid.Should().BeFalse();

        s = QueueRef.Undefined;
        s.IsNone.Should().BeFalse();
        s.IsUndefined.Should().BeTrue();
        s.IsValid.Should().BeFalse();

        s = ShardScheme.TestBackend;
        s.IsNone.Should().BeFalse();
        s.IsUndefined.Should().BeFalse();
        s.IsValid.Should().BeTrue();
    }
}
