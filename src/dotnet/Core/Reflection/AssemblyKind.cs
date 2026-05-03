namespace ActualChat.Reflection;

#pragma warning disable CA2217 // Do not mark enums with FlagsAttribute
#pragma warning disable MA0062 // Non-flags enums should not be marked with "FlagsAttribute"

[Flags]
public enum AssemblyKind
{
    System = 1,
    ActualLab = 2,
    ActualLabRpc = 4 + ActualLab,
    App = 8,
    Other = 16,
}
