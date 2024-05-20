using ActualChat.Hosting;

namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Interface, AllowMultiple = true)]
public sealed class BackendServiceAttribute(string hostRole, ServiceMode serviceMode) : Attribute
{
    public string HostRole { get; } = hostRole;
    public ServiceMode ServiceMode { get; } = serviceMode;
    public double Priority { get; init; }
}
