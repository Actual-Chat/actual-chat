using Serilog.Core;
using Serilog.Events;

namespace ActualChat.App.Server.Logging;

/// <summary>
/// Serilog enricher that adds the current process ID to log events.
/// </summary>
public class ProcessIdLogEventEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        => logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            "ProcessID", Environment.ProcessId.ToString("D", CultureInfo.InvariantCulture)));
}
