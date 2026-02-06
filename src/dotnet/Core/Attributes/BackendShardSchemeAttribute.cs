namespace ActualChat.Attributes;

/// <summary>
/// Specifies the shard scheme for a backend service.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Interface)]
public sealed class BackendShardSchemeAttribute(string hostRole) : Attribute
{
    public string HostRole { get; } = hostRole;
}
