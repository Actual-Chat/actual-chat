using Android.Util;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace ActualChat.App.Maui;

/// <summary>
/// Adds WriteTo.AndroidLog() to the logger configuration.
/// </summary>
public static class LoggerConfigurationXamarinExtensions
{
    private const string DefaultAndroidTaggedLogOutputTemplate =
        "[{SourceContext}] {Message:l{NewLine:l}{Exception:l}";

    /// <summary>Write to the built-in Android log.</summary>
    /// <param name="sinkConfiguration">The configuration this applies to.</param>
    /// <param name="tag">Tag name used for android logger.</param>
    /// <param name="restrictedToMinimumLevel">The minimum log event level required in order to write an event to the sink.</param>
    /// <param name="outputTemplate">Output template providing the format for events</param>
    /// <param name="formatProvider">Supplies culture-specific formatting information, or null.</param>
    /// <returns>Logger configuration, allowing configuration to continue.</returns>
    /// <exception cref="T:System.ArgumentNullException">A required parameter is null.</exception>
    public static LoggerConfiguration AndroidTaggedLog(
        this LoggerSinkConfiguration sinkConfiguration,
        string tag,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
        string outputTemplate = DefaultAndroidTaggedLogOutputTemplate,
        IFormatProvider? formatProvider = null)
    {
        if (sinkConfiguration == null)
            throw new ArgumentNullException(nameof(sinkConfiguration));

        var templateTextFormatter = outputTemplate != null
            ? new MessageTemplateTextFormatter(outputTemplate, formatProvider)
            : throw new ArgumentNullException(nameof(outputTemplate));
        return sinkConfiguration.Sink(new AndroidTaggedLogSink(tag, templateTextFormatter), restrictedToMinimumLevel);
    }
}

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
