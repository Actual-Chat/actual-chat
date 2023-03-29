using Newtonsoft.Json;
using Stl.Fusion.Bridge;
using Stl.Fusion.Interception;

namespace ActualChat.UI.Blazor.Services;

public class PersistentStorageReplicaCache : ReplicaCache
{
    public record Options
    {
        public ITextSerializer KeySerializer { get; } =
            // new SystemJsonSerializer(new JsonSerializerOptions() { WriteIndented = false });
            new NewtonsoftJsonSerializer(new JsonSerializerSettings() { Formatting = Formatting.None });
        public ITextSerializer ValueSerializer { get; } =
            // SystemJsonSerializer.Default;
            new NewtonsoftJsonSerializer();
    }

    private readonly Stopwatch _totalGetElapsed;
    private readonly object _lock = new object();
    private int _activeGets;
    private long _totalGetNumber;
    private long _totalSetElapsed;
    private long _totalSetNumber;

    private IReplicaCacheStore Store { get; }
    private Options Settings { get; }
    private ITextSerializer KeySerializer => Settings.KeySerializer;
    private ITextSerializer ValueSerializer => Settings.ValueSerializer;

    public PersistentStorageReplicaCache(Options settings, IServiceProvider services)
        : base(services)
    {
        _totalGetElapsed = new Stopwatch();
        Store = services.GetRequiredService<IReplicaCacheStore>();
        Settings = settings;
    }

    protected override async ValueTask<Result<T>?> GetInternal<T>(ComputeMethodInput input, CancellationToken cancellationToken)
    {
        var key = GetKey(input);
        var sw = Stopwatch.StartNew();
        long totalElapsed;
        long totalNumber;
        lock (_lock) {
            totalElapsed = _totalGetElapsed.ElapsedMilliseconds;
            if (_activeGets == 0)
                _totalGetElapsed.Start();
            _activeGets++;
        }
        var sValue = await Store.TryGetValue(key);
        lock (_lock) {
            _activeGets--;
            if (_activeGets == 0)
                _totalGetElapsed.Stop();
            totalNumber = ++_totalGetNumber;
        }
        sw.Stop();
        var elapsed = sw.ElapsedMilliseconds;
        Log.LogInformation("Get from store {Duration}/{TotalElapsed}ms ({Number}) -> ({Key})",
            elapsed, totalElapsed + elapsed, totalNumber, key);

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
        // It seems if we stores an error output, then later after restoring we always get ReplicaException.
        if (output.HasError)
            return;
        var key = GetKey(input);
        var value = ValueSerializer.Write(output);
        var sw = Stopwatch.StartNew();
        await Store.SetValue(key, value);
        sw.Stop();
        var elapsed = sw.ElapsedMilliseconds;
        Interlocked.Add(ref _totalSetElapsed, elapsed);
        Interlocked.Increment(ref _totalSetNumber);
        Log.LogInformation("Save to store {Duration}/{TotalElapsed}ms ({Number}) -> ({Key})",
            elapsed, _totalSetElapsed, _totalSetNumber, key);
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
