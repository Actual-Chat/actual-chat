using Android.Util;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace ActualChat.Maui;

// Writes events to Android.Util.Log.
public class AndroidTaggedLogSink(string tag, ITextFormatter textFormatter) : ILogEventSink
{
    public void Emit(LogEvent logEvent)
    {
        if (logEvent == null)
            throw new ArgumentNullException(nameof(logEvent));

        // A sink must never throw: every Android.Util.Log call below is a JNI transition that can fail,
        // and the throw would propagate into the logging pipeline and re-enter it via FirstChanceException.
        try {
            var output = new StringWriter();
            textFormatter.Format(logEvent, output);
            switch (logEvent.Level) {
            case LogEventLevel.Verbose:
                Log.Verbose(tag, output.ToString());
                break;
            case LogEventLevel.Debug:
                Log.Debug(tag, output.ToString());
                break;
            case LogEventLevel.Information:
                Log.Info(tag, output.ToString());
                break;
            case LogEventLevel.Warning:
                Log.Warn(tag, output.ToString());
                break;
            case LogEventLevel.Error:
                Log.Error(tag, output.ToString());
                break;
            case LogEventLevel.Fatal:
                Log.Wtf(tag, output.ToString());
                break;
            default:
                Log.WriteLine(LogPriority.Assert, tag, output.ToString());
                break;
            }
        }
        catch {
            // Intentionally ignored
        }
    }
}
