namespace ActualChat.Hosting;

/// <summary>
/// Container for registered <see cref="HostModule"/> instances.
/// </summary>
public sealed class ModuleHost
{
    public IReadOnlyList<HostModule> Modules { get; }
    public IReadOnlyDictionary<Type, HostModule> ModuleByType { get; }

    internal ModuleHost(IReadOnlyList<HostModule> modules)
    {
        Modules = modules;
        ModuleByType = modules.ToDictionary(m => m.GetType());
    }

    public T GetModule<T>()
        where T : HostModule
        => (T)ModuleByType[typeof(T)];
}
