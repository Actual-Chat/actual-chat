using Serilog;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace ActualChat.Maui;

// Adds WriteTo.AppleLog() to the logger configuration
public static class LoggerConfigurationExtensions
{
    public const string DefaultAppleLogOutputTemplate =
        "{Level:u3} [{SourceContext}] {Message:l{NewLine:l}{Exception:l}";

    public static LoggerConfiguration AppleLog(
        this LoggerSinkConfiguration sinkConfiguration,
        LogEventLevel restrictedToMinimumLevel = LogEventLevel.Verbose,
        string outputTemplate = DefaultAppleLogOutputTemplate,
        IFormatProvider? formatProvider = null)
    {
        if (sinkConfiguration == null)
            throw new ArgumentNullException(nameof(sinkConfiguration));

        var templateTextFormatter = outputTemplate != null
            ? new MessageTemplateTextFormatter(outputTemplate, formatProvider)
            : throw new ArgumentNullException(nameof(outputTemplate));

        return sinkConfiguration.Sink(new AppleUnifiedLogSink(templateTextFormatter), restrictedToMinimumLevel);
    }
}
