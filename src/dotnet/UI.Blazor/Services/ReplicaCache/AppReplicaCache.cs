using Cysharp.Text;
using Microsoft.Toolkit.HighPerformance;
using Stl.Fusion.Bridge;
using Stl.Fusion.Interception;

namespace ActualChat.UI.Blazor.Services;

public class AppReplicaCache : ReplicaCache
{
    public sealed record Options(FlushingKeyValueStore Store)
    {
        // It's important to use a shared options instance here:
        // - https://www.meziantou.net/avoid-performance-issue-with-jsonserializer-by-reusing-the-same-instance-of-json.htm
        private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };
        private static readonly ITextSerializer Serializer = new SystemJsonSerializer(SerializerOptions);

        public ITextSerializer KeySerializer { get; } = Serializer;
        public ITextSerializer ValueSerializer { get; } = Serializer;
        public Func<ComputeMethodDef, bool> ShouldForceFlushAfterSet { get; init; } = _ => false;
    }

    private bool DebugMode => Constants.DebugMode.ReplicaCache;
    private ILogger? DebugLog => DebugMode ? Log : null;

    private readonly Task<bool> _whenReady;

    private Options Settings { get; }
    private FlushingKeyValueStore Store { get; }
    private ITextSerializer KeySerializer { get; }
    private ITextSerializer ValueSerializer { get; }
    private Func<ComputeMethodDef,bool> ShouldForceFlushAfterSet { get; }

    public AppReplicaCache(Options settings, IServiceProvider services)
        : base(services)
    {
        Settings = settings;
        Store = settings.Store;
        KeySerializer = settings.KeySerializer;
        ValueSerializer = settings.ValueSerializer;
        ShouldForceFlushAfterSet = settings.ShouldForceFlushAfterSet;
        Log.LogInformation("Store type: {StoreType}", Store.GetType().GetName());
        _whenReady = Task.Run(Initialize);
    }

    protected override async ValueTask<Result<T>?> GetInternal<T>(ComputeMethodInput input, CancellationToken cancellationToken)
    {
        var ready = await _whenReady.ConfigureAwait(false);
        if (!ready)
            return null;

        var key = GetKey(input);
        var sValue = await Store.Get(key, cancellationToken).ConfigureAwait(false);
        if (sValue == null) {
            DebugLog?.LogDebug("Get({Key}) -> miss", key);
            return null;
        }

        var output = ValueSerializer.Read<T>(sValue);
        DebugLog?.LogDebug("Get({Key}) -> {Result}", key, output);
        return output;
    }

    protected override async ValueTask SetInternal<T>(ComputeMethodInput input, Result<T>? output, CancellationToken cancellationToken)
    {
        var ready = await _whenReady.ConfigureAwait(false);
        if (!ready)
            return;

        var key = GetKey(input);
        if (output is { } vOutput && !vOutput.HasError) {
            var value = ValueSerializer.Write(vOutput.Value);
            DebugLog?.LogDebug("Set({Key}) <- {Result}", key, vOutput.Value);
            Store.Set(key, value);
        }
        else {
            var message = !output.HasValue
                ? "Set({Key}) <- clear(invalidation)"
                : "Set({Key}) <- clear(error)";
            DebugLog?.LogDebug(message, key);
            Store.Set(key, null);
        }

        if (ShouldForceFlushAfterSet(input.MethodDef))
            _ = Store.Flush(cancellationToken);
    }

    // Private methods

    private async Task<bool> Initialize()
    {
        try {
            const string appVersionCacheKey = "__appVersion";
            var appVersion = AppInfo.DisplayVersion;
            if (appVersion.IsNullOrEmpty()) {
                Log.LogWarning("Initialize: App version is undefined, no way to determine whether cached data must be cleared or not");
                return false;
            }

            var cachedAppVersion = await Store.Get(appVersionCacheKey).ConfigureAwait(false);
            Log.LogInformation("Initialize: App version: {AppVersion}, stored App version: {StoredAppVersion}",
                appVersion, cachedAppVersion);
            if (OrdinalEquals(appVersion, cachedAppVersion))
                return true;

            Log.LogInformation("Initialize: cached data is stale, clearing");
            await Store.Clear().ConfigureAwait(false);
            Store.Set(appVersionCacheKey, appVersion);
            await Store.Flush().ConfigureAwait(false);

            cachedAppVersion = await Store.Get(appVersionCacheKey).ConfigureAwait(false);
            var isInitialized = OrdinalEquals(appVersion, cachedAppVersion);
            if (!isInitialized)
                Log.LogWarning("Initialize: couldn't the new App version");
            return isInitialized;
        }
        catch (Exception e) {
            Log.LogError(e, "Initialize failed");
            return false;
        }
    }

    private HashedString GetKey(ComputeMethodInput input)
    {
        var arguments = input.Arguments;
        var ctIndex = input.MethodDef.CancellationTokenArgumentIndex;
        if (ctIndex >= 0)
            arguments = arguments.Remove(ctIndex);

        var service = input.Service.GetType().NonProxyType().GetName(true, true);
        var method = input.MethodDef.Method.Name;
        var argumentsJson = KeySerializer.Write(arguments, arguments.GetType());
        var key = $"{method} @ {service} <- {argumentsJson}";
        var hashCode = key.GetDjb2HashCode();
        // The key is constructed this way to speed up index lookups
        return new HashedString(hashCode, ZString.Concat(hashCode.Format(), ": ", key));
    }
}
