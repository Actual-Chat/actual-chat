using Serilog.Core;
using Serilog.Events;

namespace ActualChat.App.Server;

public class ThreadIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        => logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            "ThreadID", Thread.CurrentThread.ManagedThreadId.ToString("D4", CultureInfo.InvariantCulture)));
}
