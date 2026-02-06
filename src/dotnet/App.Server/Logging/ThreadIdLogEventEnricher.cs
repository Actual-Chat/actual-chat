using Serilog.Core;
using Serilog.Events;

namespace ActualChat.App.Server.Logging;

/// <summary>
/// Serilog enricher that adds the current managed thread ID to log events.
/// </summary>
public class ThreadIdLogEventEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        => logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            "ThreadID", Environment.CurrentManagedThreadId.ToString("D4", CultureInfo.InvariantCulture)));
}
