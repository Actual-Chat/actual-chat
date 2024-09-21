using System.Runtime.ExceptionServices;

namespace ActualChat.UI.Blazor.App;

public static class FirstChanceExceptionLogger
{
    private static readonly object Lock = new();
    private static LogBox? _logBox;

    public static void Use()
        => AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        var error = e.Exception;
        if (error is OperationCanceledException)
            return; // This one has to be skipped

        var now = CpuTimestamp.Now;
        // ReSharper disable once InconsistentlySynchronizedField
        var logBox = _logBox;
        if (logBox == null || logBox.ExpiresAt <= now) {
            lock (Lock) {
                logBox = _logBox;
                if (logBox == null || logBox.ExpiresAt <= now)
                    logBox = _logBox = new LogBox(StaticLog.Factory.CreateLogger("FCE"), now);
            }
        }
        var stackTrace = error.StackTrace ?? new StackTrace().ToString();
        logBox.Log.LogWarning("{Type}, {Message}\r\n{StackTrace}", error.GetType().Name, error.Message, stackTrace);
    }

    // Nested types

    private sealed record LogBox(ILogger Log, CpuTimestamp ExpiresAt);
}
