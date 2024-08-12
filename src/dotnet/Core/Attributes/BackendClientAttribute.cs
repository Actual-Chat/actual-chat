namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Interface)]
public sealed class BackendClientAttribute(string shardScheme) : Attribute
{
    public string ShardScheme { get; } = shardScheme;
}
