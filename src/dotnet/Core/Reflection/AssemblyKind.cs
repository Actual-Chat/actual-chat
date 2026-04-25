namespace ActualChat.Reflection;

[Flags]
public enum AssemblyKind
{
    System = 1,
    ActualLab = 2,
    ActualLabRpc = 4 + ActualLab,
    App = 8,
    Other = 16,
}
