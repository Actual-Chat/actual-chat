using System.Text;
using Microsoft.Extensions.Primitives;

namespace ActualChat.App.Wasm.Diagnostics;

public class ComputedMonitor : WorkerBase
{
    const int SummarySampleRatio = 10;
    const int LogSampleRatio = 0; // SummarySampleRatio * 3; // Must be a multiple of SummarySampleRatio

    private int _registered;
    private int _unregistered;
    private readonly Dictionary<object, int> _summary = new();

    private IServiceProvider Services { get; }
    private ILogger Log { get; }

    public ComputedMonitor(IServiceProvider services) : base()
    {
        Services = services;
        Log = services.LogFor(GetType());
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var registry = ComputedRegistry.Instance;
        registry.OnRegister = OnRegister;
        registry.OnUnregister = OnUnregister;
        Log.LogInformation("Running");

        var sb = new StringBuilder();
        while (!cancellationToken.IsCancellationRequested) {
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
            var registered = Interlocked.Exchange(ref _registered, 0);
            var unregistered = Interlocked.Exchange(ref _unregistered, 0);

            // We want to format this log message as quickly as possible, so...
            if (registered != 0 && unregistered != 0) {
                sb.Append("Registered: +");
                sb.Append(registered);
                sb.Append(" -");
                sb.Append(unregistered);
            }
            lock (_summary) {
                if (_summary.Count != 0) {
                    if (sb.Length != 0)
                        sb.Append("\r\n");
                    sb.Append("Registration frequencies:");
                    foreach (var (key, count) in _summary) {
                        sb.Append("\r\n- ");
                        sb.Append(key);
                        sb.Append(": ");
                        sb.Append(count * SummarySampleRatio);
                    }
                    _summary.Clear();
                }
            }
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogInformation(sb.ToString());
            sb.Clear();
        }
    }

    private void OnRegister(IComputed computed)
    {
        var registered = Interlocked.Increment(ref _registered);
        if (SummarySampleRatio != 0 && registered % SummarySampleRatio == 0) {
            var id = computed.Input.ToString();
            if (id.OrdinalStartsWith("Intercepted:"))
                id = id[12..];
            var bracketIndex = id.IndexOf('(');
            var key = bracketIndex < 0 ? id : id[..bracketIndex];
            lock (_summary) {
                if (_summary.TryGetValue(key, out var count))
                    _summary[key] = count + 1;
                else
                    _summary[key] = 1;
            }
            if (LogSampleRatio != 0 && registered % LogSampleRatio == 0)
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                Log.LogDebug("+ " + id);
        }
    }

    private void OnUnregister(IComputed computed)
        => Interlocked.Increment(ref _unregistered);
}
