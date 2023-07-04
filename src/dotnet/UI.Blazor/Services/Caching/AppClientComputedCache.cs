using ActualChat.Kvas;
using ActualChat.Users;
using Stl.Fusion.Client.Caching;
using Stl.Fusion.Interception;
using Stl.Rpc;
using Stl.Rpc.Caching;

namespace ActualChat.UI.Blazor.Services;

#pragma warning disable MA0056

public abstract class AppClientComputedCache : BatchingKvas, IClientComputedCache
{
    public new record Options : BatchingKvas.Options
    {
        public string Version { get; init; } = Constants.Api.Version;
        public ImmutableHashSet<(Symbol, Symbol)> ForceFlushFor { get; init; } =
            ImmutableHashSet<(Symbol, Symbol)>.Empty.Add((nameof(IAccounts), nameof(IAccounts.GetOwn)));
    }

    protected static TextOrBytes? Miss = default;

    protected new Options Settings { get; }
    protected HashSet<(Symbol, Symbol)> ForceFlushFor { get; }
    protected RpcHub Hub { get; }
    protected RpcArgumentSerializer ArgumentSerializer { get; }
    protected bool DebugMode => Constants.DebugMode.ClientComputedCache;
    protected ILogger? DebugLog => DebugMode ? Log : null;

    public Task WhenInitialized { get; protected set; } = Task.CompletedTask;

    protected AppClientComputedCache(Options settings, IServiceProvider services)
        : base(settings, services)
    {
        Settings = settings;
        Hub = services.RpcHub();
        ArgumentSerializer = Hub.InternalServices.ArgumentSerializer;
        ForceFlushFor = new HashSet<(Symbol, Symbol)>(Settings.ForceFlushFor); // Read-only copy
    }

    public async ValueTask<(T Value, TextOrBytes Data)?> Get<T>(ComputeMethodInput input, RpcCacheKey key, CancellationToken cancellationToken)
    {
        var serviceDef = Hub.ServiceRegistry.Get(key.Service);
        var methodDef = serviceDef?.Get(key.Method);
        if (methodDef == null)
            return null;

        try {
            var resultDataOpt = await Get(key, cancellationToken).ConfigureAwait(false);
            if (resultDataOpt is not { } resultData)
                return null;

            var resultList = methodDef.ResultListFactory.Invoke();
            ArgumentSerializer.Deserialize(ref resultList, methodDef.AllowResultPolymorphism, resultData);
            return (resultList.Get0<T>(), resultData);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log?.LogError(e, "Cached result read failed");
            return null;
        }
    }

    public async ValueTask<TextOrBytes?> Get(RpcCacheKey key, CancellationToken cancellationToken = default)
    {
        if (!WhenInitialized.IsCompleted)
            return null;

        var value = await Get(key.ToString(), cancellationToken).ConfigureAwait(false);
        var result = value != null ? new TextOrBytes(value) : Miss;
        DebugLog?.LogDebug("Get({Key}) -> {Result}", key, result.HasValue ? "hit" : "miss");
        return result;
    }

    public void Set(RpcCacheKey key, TextOrBytes value)
    {
        if (!WhenInitialized.IsCompleted)
            return;

        DebugLog?.LogDebug("Set({Key})", key);
        _ = Set(key.ToString(), value.Bytes);
        if (ForceFlushFor.Contains((key.Service, key.Method)))
            _ = Flush();
    }

    public void Remove(RpcCacheKey key)
    {
        if (!WhenInitialized.IsCompleted)
            return;

        DebugLog?.LogDebug("Remove({Key})", key);
        _ = Set(key.ToString(), null);
        if (ForceFlushFor.Contains((key.Service, key.Method)))
            _ = Flush();
    }

    public override Task Clear(CancellationToken cancellationToken = default)
    {
        if (!WhenInitialized.IsCompleted)
            return Task.CompletedTask;

        DebugLog?.LogDebug("Clear()");
        return base.Clear(cancellationToken);
    }
}
