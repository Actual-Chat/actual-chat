using Serilog.Core;
using Serilog.Events;

namespace ActualChat.App.Maui.Services;

internal class ThreadIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var managedThreadId = Environment.CurrentManagedThreadId.ToString("D4", CultureInfo.InvariantCulture);
        var threadId = managedThreadId;
#if ANDROID
        var myTid = Android.OS.Process.MyTid();
        threadId = threadId + "-" + myTid;
#endif
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ThreadID", threadId));
    }
}
