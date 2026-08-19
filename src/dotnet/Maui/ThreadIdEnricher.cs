using Serilog.Core;
using Serilog.Events;

namespace ActualChat.Maui;

internal class ThreadIdEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // An enricher must never throw: on Android MyTid() is a JNI call that can fail, and the throw
        // would propagate into the logging pipeline and re-enter it via FirstChanceException.
        try {
            var threadId = Environment.CurrentManagedThreadId.ToString("D4");
#if ANDROID
            threadId = threadId + "-" + Android.OS.Process.MyTid();
#endif
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ThreadID", threadId));
        }
        catch {
            // Intentionally ignored
        }
    }
}
