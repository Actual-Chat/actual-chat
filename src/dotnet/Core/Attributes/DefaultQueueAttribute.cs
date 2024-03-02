namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class)]
public sealed class DefaultQueueAttribute(string queue) : Attribute
{
    public string Queue { get; } = queue;
}
