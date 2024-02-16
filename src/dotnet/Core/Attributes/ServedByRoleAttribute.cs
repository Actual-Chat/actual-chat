namespace ActualChat.Attributes;

[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = true)]
public sealed class ServedByRoleAttribute(string role) : Attribute
{
    public string Role { get; } = role;
}
