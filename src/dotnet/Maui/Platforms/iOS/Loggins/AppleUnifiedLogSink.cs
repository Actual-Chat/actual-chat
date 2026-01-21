using ActualChat.Maui;
using CoreFoundation;
using Foundation;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace ActualChat.Maui;

// Writes events to OSLog.
public class AppleUnifiedLogSink(ITextFormatter textFormatter) : ILogEventSink, IDisposable
{
    // probably scope could be category, but good for now
    private readonly CoreFoundation.OSLog _osLog = new (NSBundle.MainBundle.BundleIdentifier, MauiDiagnostics.LogTag);

    private readonly ITextFormatter _textFormatter = textFormatter ?? throw new ArgumentNullException(nameof(textFormatter));

    public void Emit(LogEvent logEvent)
    {
        if (logEvent == null)
            throw new ArgumentNullException(nameof(logEvent));

        using var output = new StringWriter();
        _textFormatter.Format(logEvent, output);
        // only 3 levels are working for now
        var osLogLevel = logEvent.Level switch {
            LogEventLevel.Fatal => OSLogLevel.Fault,
            LogEventLevel.Error => OSLogLevel.Fault,
            LogEventLevel.Warning => OSLogLevel.Error,
            _ => OSLogLevel.Default,
        };
        _osLog.Log(osLogLevel, output.ToString());
    }

    public void Dispose()
        => _osLog.DisposeSilently();
}
