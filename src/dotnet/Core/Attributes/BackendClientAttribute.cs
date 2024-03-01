namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class)]
public sealed class BackendClientAttribute(string hostedByRole) : Attribute
{
    public string HostedByRole { get; } = hostedByRole;
}
