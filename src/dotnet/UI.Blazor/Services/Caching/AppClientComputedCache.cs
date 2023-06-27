using ActualChat.Users;
using Stl.Fusion.Client.Caching;
using Stl.IO;
using Stl.Rpc.Caching;

namespace ActualChat.UI.Blazor.Services;

#pragma warning disable MA0056

public abstract class AppClientComputedCache : FlushingClientComputedCache
{
    public new record Options : FlushingClientComputedCache.Options
    {
        public static new Options Default { get; set; } = new();

        public FilePath DbPath { get; init; }
        public ImmutableHashSet<(Symbol, Symbol)> ForceFlushFor { get; init; } =
            ImmutableHashSet<(Symbol, Symbol)>.Empty.Add((nameof(IAccounts), nameof(IAccounts.GetOwn)));

        public Options()
        {
            Version = Constants.Api.Version;
            FlushDelay = TimeSpan.FromSeconds(0.5);
        }
    }

    protected new Options Settings { get; }
    protected HashSet<(Symbol, Symbol)> ForceFlushFor;
    protected bool DebugMode => Constants.DebugMode.ClientComputeCache;
    protected ILogger? DebugLog => DebugMode ? Log : null;

    protected AppClientComputedCache(Options settings, IServiceProvider services, bool initialize = true)
        : base(settings, services, false)
    {
        Settings = settings;
        ForceFlushFor = new HashSet<(Symbol, Symbol)>(Settings.ForceFlushFor);
        if (initialize)
            // ReSharper disable once VirtualMemberCallInConstructor
            WhenInitialized = Initialize(settings.Version);
    }

    public override async ValueTask<TextOrBytes?> Get(RpcCacheKey key, CancellationToken cancellationToken = default)
    {
        var result = await base.Get(key, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogDebug("Get({Key}) -> {Result}", key, result.HasValue ? "hit" : "miss");
        return result;
    }

    public override void Set(RpcCacheKey key, TextOrBytes value)
    {
        DebugLog?.LogDebug("Set({Key})", key);
        base.Set(key, value);
        if (ForceFlushFor.Contains((key.Service, key.Method)))
            _ = Flush();
    }

    public override void Remove(RpcCacheKey key)
    {
        DebugLog?.LogDebug("Remove({Key})", key);
        base.Remove(key);
        if (ForceFlushFor.Contains((key.Service, key.Method)))
            _ = Flush();
    }
}
