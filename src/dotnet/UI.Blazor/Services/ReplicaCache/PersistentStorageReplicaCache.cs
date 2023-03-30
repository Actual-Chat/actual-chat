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
    }

    protected override async ValueTask<Result<T>?> GetInternal<T>(ComputeMethodInput input, CancellationToken cancellationToken)
    {
        var key = GetKey(input);
        var sValue = await Storage.TryGetValue(key);
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
        // It seems that if we stored an error output, then later after restoring we always get ReplicaException.
        if (output.HasError)
            return;
        var key = GetKey(input);
        var value = ValueSerializer.Write(output);
        await Storage.SetValue(key, value);
    }

    // Private methods

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
