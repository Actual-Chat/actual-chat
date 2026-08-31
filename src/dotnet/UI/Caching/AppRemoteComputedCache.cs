using ActualChat.Kvas;
using ActualLab.Diagnostics;
using ActualLab.Fusion.Client.Caching;
using ActualLab.Fusion.Interception;
using ActualLab.Rpc;
using ActualLab.Rpc.Caching;
using ActualLab.Rpc.Serialization;

namespace ActualChat.UI.Caching;

/// <summary>
/// Abstract base for client-side caching of remote computed values.
/// Descendants provide the byte-level storage via <see cref="Store"/>.
/// </summary>
public abstract class AppRemoteComputedCache : SafeAsyncDisposableBase, IRemoteComputedCache
{
    public record Options
    {
        public string Version { get; init; } = ApiConstants.VersionString;
        public ImmutableHashSet<Symbol> ForceFlushFor { get; init; } = ImmutableHashSet<Symbol>.Empty
            .Add(RpcMethodDef.ComposeFullName(nameof(IAccounts), nameof(IAccounts.GetOwn)));
        public TimeSpan ActivationTimeout { get; init; } = TimeSpan.FromSeconds(2);
    }

    protected Options Settings { get; }
    protected HashSet<Symbol> ForceFlushFor { get; }
    protected RpcHub Hub { get; }
    protected RpcArgumentSerializer ArgumentSerializer
        => field ??= Hub.SerializationFormats.DefaultFormat.ArgumentSerializer;
    protected RpcMethodResolver AnyMethodResolver
        => field ??= Hub.ServiceRegistry.AnyMethodResolver;
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected ILogger? DebugLog
        => Constants.DebugMode.RemoteComputedCache ? Log.IfEnabled(LogLevel.Debug) : null;

    protected Task WhenReady
        // One shared deadline, so a store that never activates costs a launch one wait, not one per lookup
        => field ??= WhenInitialized.WaitAsync(Settings.ActivationTimeout);

    public IServiceProvider Services { get; }
    public IKvasStore Store { get; protected init; } = null!; // Must be set by descendant!
    public Task WhenInitialized { get; protected set; } = Task.CompletedTask;

    protected AppRemoteComputedCache(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Hub = services.RpcHub();
        ForceFlushFor = [..Settings.ForceFlushFor]; // Read-only copy
    }

    protected override Task DisposeAsync(bool disposing)
        => Store is IAsyncDisposable disposable
            ? disposable.DisposeAsync().AsTask()
            : Task.CompletedTask;

    public async ValueTask<RpcCacheEntry?> Get(
        ComputeMethodInput input, RpcCacheKey key, CancellationToken cancellationToken)
    {
        var methodDef = AnyMethodResolver[key.Name];
        if (methodDef == null)
            return null;

        try {
            var cacheValue = await Get(key, cancellationToken).ConfigureAwait(false);
            if (cacheValue is null)
                return null;

            var resultList = methodDef.ResultListType.Factory.Invoke();
            ArgumentSerializer.Deserialize(ref resultList, methodDef.HasPolymorphicResult, cacheValue.Data);
            return new(key, cacheValue, resultList.Get0Untyped());
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning("Read failed for key `{Key}`: {Type}({Message})",
                key, e.GetType().GetName(), e.Message);
            return null;
        }
    }

    public async ValueTask<RpcCacheValue?> Get(RpcCacheKey key, CancellationToken cancellationToken = default)
    {
        if (!WhenInitialized.IsCompleted) {
            // The store is picked by session and the session id comes from storage, so a launch's
            // first lookups can beat Activate there - and a miss costs a needless round trip.
            var whenReady = WhenReady;
            if (!whenReady.IsCompleted)
                await whenReady.SilentAwait(false);
            if (!WhenInitialized.IsCompleted)
                return null;
        }

        var bytes = await Store.Get(key.ToString(), cancellationToken).ConfigureAwait(false);
        var cacheValue = FromBytes(bytes);
        DebugLog?.LogDebug("Get({Key}) -> {Result}", key, cacheValue is null ? "miss" : "hit");
        return cacheValue;
    }

    public void Set(RpcCacheKey key, RpcCacheValue value)
    {
        if (!WhenInitialized.IsCompleted)
            return;

        DebugLog?.LogDebug("Set({Key})", key);
        _ = Store.Set(key.ToString(), ToBytes(value));
        if (ForceFlushFor.Contains(key.Name))
            _ = Store.Flush();
    }

    public void Remove(RpcCacheKey key)
    {
        if (!WhenInitialized.IsCompleted)
            return;

        DebugLog?.LogDebug("Remove({Key})", key);
        _ = Store.Set(key.ToString(), null);
        if (ForceFlushFor.Contains(key.Name))
            _ = Store.Flush();
    }

    public Task Clear(CancellationToken cancellationToken = default)
    {
        if (!WhenInitialized.IsCompleted)
            return Task.CompletedTask;

        DebugLog?.LogDebug("Clear()");
        return Store.Clear(cancellationToken);
    }

    // Protected methods

    protected static byte[]? ToBytes(RpcCacheValue? cacheValue)
    {
        if (cacheValue is null)
            return null;

        var hash = cacheValue.Hash;
        var data = cacheValue.Data;
        var hashPrefixLength = 1 + (hash.Length << 1);
        var result = new byte[hashPrefixLength + data.Length];
        // Hash prefix
        result[0] = checked((byte)hash.Length);
        var hashBytes = MemoryMarshal.Cast<char, byte>(hash.AsSpan());
        hashBytes.CopyTo(result.AsSpan(1));
        // Data suffix
        data.Span.CopyTo(result.AsSpan(hashPrefixLength));
        return result;
    }

    protected RpcCacheValue? FromBytes(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < 1)
            return null;

        var byteHashLength = bytes[0] << 1;
        if (byteHashLength == 0) // Empty hash
            return new RpcCacheValue(bytes.AsMemory(1), "");

        if (byteHashLength != 48) {
            // The current hash length is 24 characters
            DebugLog?.LogWarning("Invalid hash length: {HashLength}", byteHashLength >> 1);
            return null;
        }

        var span = bytes.AsSpan(1);
        var hash = new string(MemoryMarshal.Cast<byte, char>(span[..byteHashLength]));
        var data = bytes.AsMemory(1 + byteHashLength);
        return new RpcCacheValue(data, hash);
    }
}
