using Stl.Fusion.Interception;

namespace ActualChat.UI.Blazor.Services;

public interface IAppReplicaCacheConfigurator
{
    void ForceFlush(Type serviceType, string methodName);
}

public class AppReplicaCacheConfigurator : IAppReplicaCacheConfigurator
{
    private readonly ConcurrentDictionary<Type, HashSet<string>> _forceFlush = new ();

    public void ForceFlush(Type serviceType, string methodName)
    {
        _forceFlush.TryAdd(serviceType, new HashSet<string>(StringComparer.Ordinal));
        var set = _forceFlush[serviceType];
        set.Add(methodName);
    }

    public bool ShouldForceFlushAfterSet(ComputeMethodDef def)
    {
        var mi = def.Method;
        if (!_forceFlush.TryGetValue(mi.DeclaringType!, out var methods))
            return false;
        return methods.Contains(mi.Name);
    }

    public AppReplicaCacheConfigurator(IEnumerable<Action<IAppReplicaCacheConfigurator>> configureActions)
    {
        foreach (var configureAction in configureActions)
            configureAction.Invoke(this);
    }
}
