using ActualChat.Hosting;

namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true)]
public sealed class ServiceModeAttribute(string hostRole, ServiceMode serviceMode, double priority = 0) : Attribute
{
    public string HostRole { get; } = hostRole;
    public ServiceMode ServiceMode { get; } = serviceMode;
    public double Priority { get; } = priority;
}
