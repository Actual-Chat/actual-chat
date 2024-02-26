namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class)]
public sealed class ShardSchemeAttribute(string shardScheme) : Attribute
{
    public string ShardScheme { get; } = shardScheme;
}
