namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Interface)]
public sealed class BackendClientAttribute(string hostRole) : Attribute
{
    public string HostRole { get; } = hostRole;
}
