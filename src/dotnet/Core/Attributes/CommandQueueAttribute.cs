namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class)]
public sealed class CommandQueueAttribute(string queueShardScheme) : Attribute
{
    public string QueueShardScheme { get; } = queueShardScheme;
}
