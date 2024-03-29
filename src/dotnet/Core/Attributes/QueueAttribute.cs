namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class QueueAttribute(string shardScheme) : Attribute
{
    public string ShardScheme { get; } = shardScheme;
}
