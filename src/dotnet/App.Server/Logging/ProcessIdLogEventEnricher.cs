using Serilog.Core;
using Serilog.Events;

namespace ActualChat.App.Server.Logging;

public class ProcessIdLogEventEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        => logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            "ProcessID", Environment.ProcessId.ToString("D", CultureInfo.InvariantCulture)));
}
