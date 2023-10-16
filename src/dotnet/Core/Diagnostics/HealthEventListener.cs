using System.Diagnostics.Tracing;

namespace ActualChat.Diagnostics;

public class HealthEventListener(IServiceProvider services, int interval = 10) : EventListener, IRuntimeStats
{
    private const double Epsilon = 0.01d;

    private readonly Queue<double> _last5Values = new ();
    private readonly Queue<double> _last20Values = new ();
    private readonly IMutableState<double> _cpuMean = services.StateFactory().NewMutable<double>();
    private readonly IMutableState<double> _cpuMean5 = services.StateFactory().NewMutable<double>();
    private readonly IMutableState<double> _cpuMean20 = services.StateFactory().NewMutable<double>();

    public IState<double> CpuMean => _cpuMean;
    public IState<double> CpuMean5 => _cpuMean5;
    public IState<double> CpuMean20 => _cpuMean20;

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (!source.Name.Equals("System.Runtime"))
            return;

        var refreshInterval = new Dictionary<string, string> { { "EventCounterIntervalSec", interval.ToString(CultureInfo.InvariantCulture) } };
        EnableEvents(source, EventLevel.Verbose, EventKeywords.All, refreshInterval!);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventName is not "EventCounters")
            return;

        if (eventData.Payload == null)
            return;

        for (int i = 0; i < eventData.Payload.Count; i++) {
            var eventPayload = eventData.Payload[i] as IDictionary<string, object>;
            if (eventPayload == null || !ReferenceEquals(eventPayload["Name"], "cpu-usage"))
                continue;

            var currentCpuMean = _cpuMean.Value;
            var cpuMean = (double)eventPayload["Mean"];
            if (Math.Abs(currentCpuMean - cpuMean) >= Epsilon)
                _cpuMean.Value = cpuMean;

            _last5Values.Enqueue(cpuMean);
            _last20Values.Enqueue(cpuMean);
            if (_last5Values.Count > 5)
                _last5Values.Dequeue();
            if (_last20Values.Count > 20)
                _last20Values.Dequeue();

            // assuming it is safe to use thread unsafe operations as this method will be called once per provided interval
            var cpuMean5 = _last5Values.Sum() / _last5Values.Count;
            var cpuMean20 = _last20Values.Sum() / _last20Values.Count;
            var currentCpuMean5 = _cpuMean5.Value;
            var currentCpuMean20 = _cpuMean20.Value;
            if (Math.Abs(currentCpuMean5 - cpuMean5) >= Epsilon)
                _cpuMean5.Value = cpuMean5;
            if (Math.Abs(currentCpuMean20 - cpuMean20) >= Epsilon)
                _cpuMean20.Value = cpuMean20;
        }
    }
}
