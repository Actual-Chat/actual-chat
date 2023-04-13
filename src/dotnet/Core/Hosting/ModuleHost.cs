namespace ActualChat.Hosting;

public sealed class ModuleHost
{
    private readonly ImmutableArray<HostModule> _sortedModules;
    private readonly Dictionary<Type, HostModule> _moduleMap;

    internal ModuleHost(params HostModule[] modules)
    {
        _sortedModules = modules.ToImmutableArray();
        _moduleMap = modules.ToDictionary(m => m.GetType());
        foreach (var module in _sortedModules)
            module.SetHost(this);
    }

    public T GetModule<T>() where T : HostModule
        => (T)_moduleMap[typeof(T)];

    public ImmutableArray<HostModule> GetModules()
        => _sortedModules;

    public ImmutableArray<T> GetModules<T>()
        => _sortedModules.OfType<T>().ToImmutableArray();
}
