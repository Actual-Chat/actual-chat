using ActualLab.Interception;

namespace ActualChat;

public readonly record struct Requester(
    object? Target,
    Func<object?, string>? Formatter = null)
{
    public override string ToString()
    {
        if (Formatter != null)
            return Formatter.Invoke(Target);

        if (ReferenceEquals(Target, null))
            return "Unknown";
        if (Target is MethodDef methodDef)
            return methodDef.ToString();

        return Target.GetType().NonProxyType().GetName();
    }
}
