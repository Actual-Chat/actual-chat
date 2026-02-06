namespace ActualChat.Attributes;

/// <summary>
/// Specifies the shard scheme used for queue-based command processing.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class QueueAttribute(string shardScheme) : Attribute
{
    public string ShardScheme { get; } = shardScheme;
}
