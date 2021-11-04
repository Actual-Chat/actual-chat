
namespace ActualChat;

/// <summary> Marks a controller or a method as for internal network usage only </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Interface)]
public class InternalAttribute : Attribute
{
}
