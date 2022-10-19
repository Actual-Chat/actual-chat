using ActualChat.Commands;

namespace ActualChat.Core.UnitTests.Commands;

public class QueueRefTest
{
    [Theory]
    [InlineData("QN", "K1", CommandPriority.Critical, "QN[K1].critical")]
    [InlineData("QN", "K1", CommandPriority.Default, "QN[K1]")]
    [InlineData("QN", "K1", CommandPriority.High, "QN[K1].high")]
    [InlineData("QN", "K1", CommandPriority.Low, "QN[K1].low")]
    [InlineData("QN", "", CommandPriority.Critical, "QN.critical")]
    [InlineData("QN", "", CommandPriority.Low, "QN.low")]
    [InlineData("QN", "", CommandPriority.Default, "QN")]
    [InlineData("QN", null, CommandPriority.Critical, "QN.critical")]
    [InlineData("QN", null, CommandPriority.Low, "QN.low")]
    [InlineData("QN", null, CommandPriority.Default, "QN")]
    public void CombinedTest(string queueName, string? shardKey, CommandPriority priority, string format)
    {
        var queueRef = new QueueRef(queueName, shardKey, priority);
        queueRef.ToString().Should().Be(format);
        queueRef.Should().Be(queueRef);

        var queueRef1 = QueueRef.Parse(format);
        queueRef1.Should().Be(queueRef);

        queueRef.AssertPassesThroughAllSerializers();
    }
}
