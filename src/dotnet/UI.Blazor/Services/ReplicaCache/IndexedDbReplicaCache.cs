using ActualChat.UI.Blazor.Module;
using Stl.Fusion.Bridge;
using Stl.Fusion.Interception;

namespace ActualChat.UI.Blazor.Services;

public class IndexedDbReplicaCache : ReplicaCache
{
    public record Options
    {
        public ITextSerializer KeySerializer { get; } =
            new SystemJsonSerializer(new JsonSerializerOptions() { WriteIndented = false });
            // new NewtonsoftJsonSerializer(new JsonSerializerSettings() { Formatting = Formatting.None }).ToTyped<Key>();
        public ITextSerializer ValueSerializer { get; } = SystemJsonSerializer.Default;
    }

    private Func<IJSRuntime> JsAccessor { get; }
    private Options Settings { get; }
    private ITextSerializer KeySerializer => Settings.KeySerializer;
    private ITextSerializer ValueSerializer => Settings.ValueSerializer;

    public IndexedDbReplicaCache(Options settings, IServiceProvider services)
        : base(services)
    {
        JsAccessor = services.GetRequiredService<Func<IJSRuntime>>();
        Settings = settings;
    }

    protected override async ValueTask<Result<T>?> GetInternal<T>(ComputeMethodInput input, CancellationToken cancellationToken)
    {
        var key = GetKey(input);
        var sValue = await TryGetValue(key);
        if (sValue == null) {
            Log.LogInformation("Get({Key}) -> miss", key);
            return null;
        }

        var output = ValueSerializer.Read<Result<T>>(sValue);
        Log.LogInformation("Get({Key}) -> {Result}", key, output);
        return output;
    }

    protected override async ValueTask SetInternal<T>(ComputeMethodInput input, Result<T> output, CancellationToken cancellationToken)
    {
        var key = GetKey(input);
        var value = ValueSerializer.Write(output);
        await SetValue(key, value);
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

    private async Task<string?> TryGetValue(Symbol key)
    {
        var js = JsAccessor();
        return await js.InvokeAsync<string?>(
            $"{BlazorUICoreModule.ImportName}.ReplicaCache.get",
            key.Value);
    }

    private async Task SetValue(Symbol key, string value)
    {
        var js = JsAccessor();
        await js.InvokeAsync<string?>(
            $"{BlazorUICoreModule.ImportName}.ReplicaCache.set",
            key.Value,
            value);
    }
}
