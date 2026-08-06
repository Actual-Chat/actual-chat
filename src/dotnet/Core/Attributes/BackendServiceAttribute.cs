
namespace ActualChat.Attributes;

/// <summary>
/// Declares that a service runs on a specific host role with a given service mode.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Interface, AllowMultiple = true)]
public sealed class BackendServiceAttribute(string hostRole, ServiceMode serviceMode) : Attribute
{
    public string HostRole { get; } = hostRole;
    public ServiceMode ServiceMode { get; } = serviceMode;
    public double Priority { get; init; }
}
