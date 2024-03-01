namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class)]
public sealed class CommandQueueAttribute(string queueRole) : Attribute
{
    public string QueueRole { get; } = queueRole;
}
