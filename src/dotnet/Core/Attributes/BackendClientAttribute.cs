namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class)]
public sealed class BackendClientAttribute(string shardScheme) : Attribute
{
    public string ShardScheme { get; } = shardScheme;
}
