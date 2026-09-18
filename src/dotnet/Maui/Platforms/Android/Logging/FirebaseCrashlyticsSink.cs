using Firebase.Crashlytics;
using Java.Lang;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace ActualChat.Maui;

public class AndroidFirebaseCrashlyticsSink : ILogEventSink
{
    // Firebase initializes on its own thread (MauiFirebase), so the first seconds of startup log
    // before Crashlytics exists. Those are the lines an ANR report needs most, so they're held
    // and replayed once it's up; the cap bounds a start whose init fails and never gets there.
    private const int MaxPendingCount = 256;

    private readonly ITextFormatter _textFormatter =
        new MessageTemplateTextFormatter(LoggerConfigurationXamarinExtensions.DefaultAndroidTaggedLogOutputTemplate);
    private readonly Lock _lock = new();
    private Queue<LogEvent>? _pending = new();

    public AndroidFirebaseCrashlyticsSink()
        => _ = MauiFirebase.WhenReady.ContinueWith(
            _ => FlushPending(), TaskContinuationOptions.OnlyOnRanToCompletion);

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Information)
            return;

        if (!MauiFirebase.IsReady) {
            lock (_lock) {
                if (_pending is not null) {
                    if (_pending.Count >= MaxPendingCount)
                        _pending.Dequeue();
                    _pending.Enqueue(logEvent);
                    return;
                }
            }
        }
        FlushPending();
        Write(logEvent);
    }

    private void FlushPending()
    {
        Queue<LogEvent>? pending;
        lock (_lock) {
            pending = _pending;
            _pending = null;
        }
        if (pending is null)
            return;

        foreach (var logEvent in pending)
            Write(logEvent);
    }

    private void Write(LogEvent logEvent)
    {
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
