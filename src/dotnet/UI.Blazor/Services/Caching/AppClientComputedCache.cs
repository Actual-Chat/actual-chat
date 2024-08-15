using System.Diagnostics.CodeAnalysis;
using ActualChat.Kvas;
using ActualChat.Users;
using ActualLab.Fusion.Client.Caching;
using ActualLab.Fusion.Interception;
using ActualLab.Internal;
using ActualLab.Rpc;
using ActualLab.Rpc.Caching;
using ActualLab.Rpc.Serialization;

namespace ActualChat.UI.Blazor.Services;

public abstract class AppClientComputedCache : BatchingKvas, IRemoteComputedCache
{
    public new record Options : BatchingKvas.Options
    {
        public string Version { get; init; } = Constants.Api.StringVersion;
        public ImmutableHashSet<(Symbol, Symbol)> ForceFlushFor { get; init; } =
            ImmutableHashSet<(Symbol, Symbol)>.Empty.Add((nameof(IAccounts), nameof(IAccounts.GetOwn)));
    }

    protected new Options Settings { get; }
    protected HashSet<(Symbol, Symbol)> ForceFlushFor { get; }
    protected RpcHub Hub { get; }
    protected RpcArgumentSerializer ArgumentSerializer { get; }
    protected static bool DebugMode => Constants.DebugMode.RemoteComputedCache;
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

    [RequiresUnreferencedCode(UnreferencedCode.Serialization)]
    public async ValueTask<RpcCacheEntry<T>?> Get<T>(
        ComputeMethodInput input, RpcCacheKey key, CancellationToken cancellationToken)
    {
        var serviceDef = Hub.ServiceRegistry.Get(key.Service);
        var methodDef = serviceDef?.GetMethod(key.Method);
        if (methodDef == null)
            return null;

        try {
            var cacheValue = await Get(key, cancellationToken).ConfigureAwait(false);
            if (cacheValue.IsNone)
                return null;

            var resultList = methodDef.ResultListType.Factory.Invoke();
            ArgumentSerializer.Deserialize(ref resultList, methodDef.AllowResultPolymorphism, cacheValue.Data);
            return new(key, cacheValue, resultList.Get0<T>());
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Cached result read failed");
            return null;
        }
    }

    public async ValueTask<RpcCacheValue> Get(RpcCacheKey key, CancellationToken cancellationToken = default)
    {
        if (!WhenInitialized.IsCompleted)
            return default;

        var bytes = await Get(key.ToString(), cancellationToken).ConfigureAwait(false);
        var cacheValue = FromBytes(bytes);
        DebugLog?.LogDebug("Get({Key}) -> {Result}", key, cacheValue.IsNone ? "miss" : "hit");
        return cacheValue;
    }

    public void Set(RpcCacheKey key, RpcCacheValue value)
    {
        if (!WhenInitialized.IsCompleted)
            return;

        DebugLog?.LogDebug("Set({Key})", key);
        _ = Set(key.ToString(), ToBytes(value));
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

    // Protected methods

    protected static byte[]? ToBytes(RpcCacheValue cacheValue)
    {
        if (cacheValue.IsNone)
            return null;

        var hash = cacheValue.Hash;
        var data = cacheValue.Data.Bytes;
        var hashPrefixLength = 1 + (hash.Length << 1);
        var bytes = new byte[hashPrefixLength + data.Length];
        // Hash prefix
        bytes[0] = checked((byte)hash.Length);
        var hashBytes = MemoryMarshal.Cast<char, byte>(hash.AsSpan());
        hashBytes.CopyTo(bytes.AsSpan(1));
        // Data suffix
        data.CopyTo(bytes.AsSpan(hashPrefixLength));
        return bytes;
    }

    protected RpcCacheValue FromBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 1)
            return default;

        var byteHashLength = bytes[0] << 1;
        if (byteHashLength == 0) // Empty hash
            return new RpcCacheValue(new TextOrBytes(bytes.AsMemory(1)), "");

        if (byteHashLength != 48) {
            // Current hash length is 24 characters
            DebugLog?.LogWarning("Invalid hash length: {HashLength}", byteHashLength >> 1);
            return default;
        }

        var span = bytes.AsSpan(1);
        var hash = new string(MemoryMarshal.Cast<byte, char>(span[..byteHashLength]));
        var data = bytes.AsMemory(1 + byteHashLength);
        return new RpcCacheValue(new TextOrBytes(data), hash);
    }
}
