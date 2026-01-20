using Firebase.Crashlytics;
using Java.Lang;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace ActualChat.App.Maui;

public class AndroidFirebaseCrashlyticsSink : ILogEventSink
{
    private readonly ITextFormatter _textFormatter =
        new MessageTemplateTextFormatter(LoggerConfigurationXamarinExtensions.DefaultAndroidTaggedLogOutputTemplate);

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Information)
            return;

        var firebaseCrashlytics = FirebaseCrashlytics.Instance;
        if (firebaseCrashlytics is null)
            return;

        if (logEvent.Exception is Throwable throwable)
            firebaseCrashlytics.RecordException(throwable);

        var message = FormatMessage(logEvent);
        firebaseCrashlytics.Log(message);
    }

    private string FormatMessage(LogEvent logEvent)
    {
        var output = new StringWriter();
        _textFormatter.Format(logEvent, output);
        return output.ToString();
    }
}

