using ActualChat.Commands;

namespace ActualChat.Core.UnitTests.Commands;

public class QueueRefTest
{
    [Theory]
    [InlineData("QN", "K1", QueuedCommandPriority.Critical, "QN[K1].critical")]
    [InlineData("QN", "K1", QueuedCommandPriority.Default, "QN[K1]")]
    [InlineData("QN", "K1", QueuedCommandPriority.High, "QN[K1].high")]
    [InlineData("QN", "K1", QueuedCommandPriority.Low, "QN[K1].low")]
    [InlineData("QN", "", QueuedCommandPriority.Critical, "QN.critical")]
    [InlineData("QN", "", QueuedCommandPriority.Low, "QN.low")]
    [InlineData("QN", "", QueuedCommandPriority.Default, "QN")]
    [InlineData("QN", null, QueuedCommandPriority.Critical, "QN.critical")]
    [InlineData("QN", null, QueuedCommandPriority.Low, "QN.low")]
    [InlineData("QN", null, QueuedCommandPriority.Default, "QN")]
    public void CombinedTest(string queueName, string? shardKey, QueuedCommandPriority priority, string format)
    {
        var queueRef = new QueueRef(queueName, shardKey, priority);
        queueRef.ToString().Should().Be(format);
        queueRef.Should().Be(queueRef);

        var queueRef1 = QueueRef.Parse(format);
        queueRef1.Should().Be(queueRef);

        queueRef.AssertPassesThroughAllSerializers();
    }
}
