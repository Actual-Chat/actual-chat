using Serilog;
using Serilog.Configuration;
using Serilog.Events;
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
