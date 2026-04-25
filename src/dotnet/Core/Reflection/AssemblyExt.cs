namespace ActualChat.Reflection;

public static class AssemblyExt
{
    private static ConcurrentDictionary<Assembly, AssemblyKind> Cache = new();

    public static AssemblyKind GetKind(this Assembly assembly)
        => Cache.GetOrAdd(assembly, ComputeAssemblyKind);

    // Private methods

    private static AssemblyKind ComputeAssemblyKind(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        if (name.IsNullOrEmpty())
            return AssemblyKind.Other;

        if (name == "ActualChat" || name.StartsWith("ActualChat.", StringComparison.Ordinal))
            return AssemblyKind.App;

        if (name.StartsWith("ActualLab.", StringComparison.Ordinal))
            return name == "ActualLab.Rpc" ? AssemblyKind.ActualLabRpc : AssemblyKind.ActualLab;

        if (name == "mscorlib" || name == "System" || name == "netstandard"
            || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal))
            return AssemblyKind.System;

        return AssemblyKind.Other;
    }
}
