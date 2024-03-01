namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class)]
public sealed class DefaultQueueAttribute(string hostedByRole) : Attribute
{
    public string HostedByRole { get; } = hostedByRole;
}
