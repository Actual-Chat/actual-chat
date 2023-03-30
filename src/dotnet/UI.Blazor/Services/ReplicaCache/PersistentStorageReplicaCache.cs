using Newtonsoft.Json;
using Stl.Fusion.Bridge;
using Stl.Fusion.Interception;

namespace ActualChat.UI.Blazor.Services;

public class PersistentStorageReplicaCache : ReplicaCache
{
    public record Options
    {
        public ITextSerializer KeySerializer { get; } =
            new NewtonsoftJsonSerializer(new JsonSerializerSettings { Formatting = Formatting.None });
        public ITextSerializer ValueSerializer { get; } =
            new NewtonsoftJsonSerializer();
    }

    private readonly Task<bool> _whenReady;

    private IReplicaCacheStorage Storage { get; }
    private Options Settings { get; }
    private ITextSerializer KeySerializer => Settings.KeySerializer;
    private ITextSerializer ValueSerializer => Settings.ValueSerializer;

    public PersistentStorageReplicaCache(Options settings, IServiceProvider services)
        : base(services)
    {
        var storage = services.GetRequiredService<IReplicaCacheStorage>();
        Log.LogInformation("ReplicaCache storage type is '{StorageType}'", storage.GetType().FullName);
        Storage = ReplicaCacheStoragePerfMonitor.EnablePerfMonitor(true, storage, services.LogFor<ReplicaCacheStoragePerfMonitor>());
        Settings = settings;
        _whenReady = Task.Run(Init);
    }

    protected override async ValueTask<Result<T>?> GetInternal<T>(ComputeMethodInput input, CancellationToken cancellationToken)
    {
        var ready = await _whenReady.ConfigureAwait(false);
        if (!ready)
            return null;
        var key = GetKey(input);
        var sValue = await Storage.TryGetValue(key).ConfigureAwait(false);
        if (sValue == null) {
            if (Log.IsEnabled(LogLevel.Debug))
                Log.LogDebug("Get({Key}) -> miss", key);
            return null;
        }

        var output = ValueSerializer.Read<Result<T>>(sValue);
        if (Log.IsEnabled(LogLevel.Debug))
            Log.LogDebug("Get({Key}) -> {Result}", key, output);
        return output;
    }

    protected override async ValueTask SetInternal<T>(ComputeMethodInput input, Result<T> output, CancellationToken cancellationToken)
    {
        var ready = await _whenReady.ConfigureAwait(false);
        if (!ready)
            return;
        // It seems that if we stored an error output, then later after restoring we always get ReplicaException.
        if (output.HasError)
            return;

        await _whenReady.ConfigureAwait(false);
        var key = GetKey(input);
        var value = ValueSerializer.Write(output);
        await Storage.SetValue(key, value).ConfigureAwait(false);
    }

    // Private methods

    private async Task<bool> Init()
    {
        try {
            const string appVersionCacheKey = "__appVersion";
            var appVersion = AppInfo.DisplayVersion;
            if (appVersion.IsNullOrEmpty()) {
                Log.LogWarning("App version is undefined. Can not properly estimate if cached data is stale or not");
                return false;
            }
            var cachedAppVersion = await Storage.TryGetValue(appVersionCacheKey).ConfigureAwait(false);
            Log.LogInformation("Initializing. App version: '{AppVersion}'. Stored app version: '{StoredAppVersion}'",
                appVersion, cachedAppVersion);
            if (OrdinalEquals(appVersion, cachedAppVersion))
                return true;
            Log.LogInformation("Cached data is apparently stale. Will clear storage");
            await Storage.Clear().ConfigureAwait(false);
            Log.LogWarning("Store has been cleared");
            await Storage.SetValue(appVersionCacheKey, appVersion).ConfigureAwait(false);
            cachedAppVersion = await Storage.TryGetValue(appVersionCacheKey).ConfigureAwait(false);
            var successfullyInitialized = OrdinalEquals(appVersion, cachedAppVersion);
            var logLevel = successfullyInitialized ? LogLevel.Information : LogLevel.Warning;
            Log.Log(logLevel, "App version has been stored. Successfully: {Successfully}",
                successfullyInitialized);
            return successfullyInitialized;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to initialize");
            return false;
        }
    }

    private Symbol GetKey(ComputeMethodInput input)
    {
        var arguments = input.Arguments;
        var ctIndex = input.MethodDef.CancellationTokenArgumentIndex;
        if (ctIndex >= 0)
            arguments = arguments.Remove(ctIndex);

        var service = input.Service.GetType().NonProxyType().GetName(true, true);
        var method = input.MethodDef.Method.Name;
        var argumentsJson = KeySerializer.Write(arguments, arguments.GetType());
        return $"{method} @ {service} <- {argumentsJson}";
    }
}
