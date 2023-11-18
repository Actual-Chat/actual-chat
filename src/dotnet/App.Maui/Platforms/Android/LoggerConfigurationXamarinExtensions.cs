using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace ActualChat.App.Maui;

// Adds WriteTo.AndroidLog() to the logger configuration
public static class LoggerConfigurationXamarinExtensions
{
    private const string DefaultAndroidTaggedLogOutputTemplate =
        "[{SourceContext}] {Message:l{NewLine:l}{Exception:l}";

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
