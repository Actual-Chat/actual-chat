using Android.Util;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace ActualChat.App.Maui;

/// <summary>
/// Writes events to <see cref="T:Android.Util.Log" />.
/// </summary>
public class AndroidTaggedLogSink : ILogEventSink
{
    private readonly string _tag;
    private readonly ITextFormatter _textFormatter;

    /// <summary>
    /// Create an instance with the provided <see cref="T:Serilog.Formatting.ITextFormatter" />.
    /// </summary>
    /// <param name="tag">Tag name used for android logger.</param>
    /// <param name="textFormatter">Formatter for log events</param>
    /// <exception cref="T:System.ArgumentNullException">The text formatter must be provided</exception>
    public AndroidTaggedLogSink(string tag, ITextFormatter textFormatter)
    {
        _tag = tag;
        _textFormatter = textFormatter ?? throw new ArgumentNullException(nameof(textFormatter));
    }

    /// <summary>Emit the provided log event to the sink.</summary>
    /// <param name="logEvent">The log event to write.</param>
    public void Emit(LogEvent logEvent)
    {
        if (logEvent == null)
            throw new ArgumentNullException(nameof(logEvent));

        StringWriter output = new StringWriter();
        this._textFormatter.Format(logEvent, output);
        switch (logEvent.Level) {
        case LogEventLevel.Verbose:
            Android.Util.Log.Verbose(_tag, output.ToString());
            break;
        case LogEventLevel.Debug:
            Android.Util.Log.Debug(_tag, output.ToString());
            break;
        case LogEventLevel.Information:
            Android.Util.Log.Info(_tag, output.ToString());
            break;
        case LogEventLevel.Warning:
            Android.Util.Log.Warn(_tag, output.ToString());
            break;
        case LogEventLevel.Error:
            Android.Util.Log.Error(_tag, output.ToString());
            break;
        case LogEventLevel.Fatal:
            Android.Util.Log.Wtf(_tag, output.ToString());
            break;
        default:
            Android.Util.Log.WriteLine(LogPriority.Assert, _tag, output.ToString());
            break;
        }
    }
}
