using Serilog.Core;
using Serilog.Events;

namespace ActualChat.App.Server.Logging;

public class ThreadIdLogEventEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        => logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            "ThreadID", Environment.CurrentManagedThreadId.ToString("D4", CultureInfo.InvariantCulture)));
}
