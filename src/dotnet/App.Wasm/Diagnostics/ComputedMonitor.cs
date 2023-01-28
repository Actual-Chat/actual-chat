using System.Text;

namespace ActualChat.App.Wasm.Diagnostics;

public class ComputedMonitor : WorkerBase
{
    private readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(3);
    private const int SummarySampleRatio = 10;
    private const int LogSampleRatio = 0; // SummarySampleRatio * 3; // Must be a multiple of SummarySampleRatio

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
            await Task.Delay(SummaryInterval, cancellationToken).ConfigureAwait(false);
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
                    sb.Append("Update frequencies (#/s):");
                    var multiplier = SummarySampleRatio / SummaryInterval.TotalSeconds;
                    foreach (var (key, count) in _summary.OrderByDescending(kv => kv.Value)) {
                        sb.Append("\r\n- ");
                        sb.Append(key);
                        sb.Append(": ");
                        sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F1}", count * multiplier);
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
            var input = computed.Input;
            var category = input.Category;
            lock (_summary) {
                if (_summary.TryGetValue(category, out var count))
                    _summary[category] = count + 1;
                else
                    _summary[category] = 1;
            }
            if (LogSampleRatio != 0 && registered % LogSampleRatio == 0)
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                Log.LogDebug("+ " + input.ToString());
        }
    }

    private void OnUnregister(IComputed computed)
    {
        var unregistered = Interlocked.Increment(ref _unregistered);
        if (LogSampleRatio != 0 && unregistered % LogSampleRatio == 0)
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogDebug("+ " + computed.Input.ToString());
    }
}
