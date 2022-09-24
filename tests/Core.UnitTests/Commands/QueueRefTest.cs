using ActualChat.Commands;

namespace ActualChat.Core.UnitTests.Commands;

public class QueueRefTest
{
    [Theory]
    [InlineData("QN", "K1", CommandPriority.Critical, "QN[K1].critical")]
    [InlineData("QN", "K1", CommandPriority.Default, "QN[K1]")]
    [InlineData("QN", "K1", CommandPriority.High, "QN[K1].high")]
    [InlineData("QN", "K1", CommandPriority.Low, "QN[K1].low")]
    [InlineData("QN", null, CommandPriority.Critical, "QN.critical")]
    [InlineData("QN", null, CommandPriority.Low, "QN.low")]
    [InlineData("QN", null, CommandPriority.Default, "QN")]
    public void MakeValidRef(string queueName, string? shardKey, CommandPriority priority, string expectedRef)
    {
        var @ref = new QueueRef(queueName, shardKey, priority);
        @ref.Ref.Should().Be(expectedRef);
    }

    [Theory]
    [InlineData("QN", "K1", CommandPriority.Critical, "QN[K1].critical")]
    [InlineData("QN", "K1", CommandPriority.Default, "QN[K1]")]
    [InlineData("QN", "K1", CommandPriority.High, "QN[K1].high")]
    [InlineData("QN", "K1", CommandPriority.Low, "QN[K1].low")]
    [InlineData("QN", null, CommandPriority.Critical, "QN.critical")]
    [InlineData("QN", null, CommandPriority.Low, "QN.low")]
    [InlineData("QN", null, CommandPriority.Default, "QN")]
    public void ParseValidRef(string queueName, string? shardKey, CommandPriority priority, string serializedRef)
    {
        var @ref = new QueueRef(serializedRef);
        @ref.Ref.Should().Be(serializedRef);
        @ref.QueueName.Should().Be(queueName);
        @ref.ShardKey.Should().Be(shardKey);
        @ref.Priority.Should().Be(priority);
    }
}
