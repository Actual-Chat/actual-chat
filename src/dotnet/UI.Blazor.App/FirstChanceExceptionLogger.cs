using System.Net.Sockets;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;

namespace ActualChat.UI.Blazor.App;

public static class FirstChanceExceptionLogger
{
    private static readonly ILogger Log = StaticLog.Factory.CreateLogger("FCE");
    [ThreadStatic]
    private static bool _isInHandler;

    public static Func<Exception, bool> ShouldSkip { get; set; } = _ => false;
    public static bool IsActivated { get; private set; }
    public static LogLevel LogLevel { get; private set; } = LogLevel.Information;

    public static void Use(LogLevel logLevel = LogLevel.Information)
    {
        if (logLevel > LogLevel)
            LogLevel = logLevel;
        if (IsActivated)
            return;

        IsActivated = true;
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
    {
        // Every throw below - in a ShouldSkip callback, an enricher, or a sink - re-enters this handler.
        // Without this guard that recursion continues until the thread's stack overflows.
        if (_isInHandler)
            return;

        _isInHandler = true;
        try {
            LogFirstChanceException(e.Exception);
        }
        finally {
            _isInHandler = false;
        }
    }

    private static void LogFirstChanceException(Exception error)
    {
        if (error is OperationCanceledException or WebSocketException or HttpRequestException or SocketException)
            return; // This one has to be skipped
        if (error is IOException { InnerException: SocketException })
            return; // SslStream/NetworkStream wraps transient socket disconnects as IOException
        if (error is TimeoutException { Message: "Timeout while connecting to remote host." })
            return; // Transient RPC connect timeout - caught downstream, noisy on Sentry

        foreach (var func in ShouldSkip.GetInvocationList().OfType<Func<Exception, bool>>()) {
            if (func(error))
                return; // This one has to be skipped
        }

        var withStackTrace = true;
        // Handles System.IO.FileNotFoundException and Java.IO.FileNotFoundException exceptions as well
        if (error.GetType().Name == nameof(FileNotFoundException))
            if (error.Message.StartsWith("wwwroot/"))
                withStackTrace = false;

        LogInternal(error, withStackTrace);

        // NOTE(DF): Perhaps it's redundant and inner exception has been already logged with FCE earlier.
        // But I saw TargetInvocationException in Crashes reports in 'Google console/Android Vitals'
        //  and wanted to be sure that it's been logged to find details of a crash.
        if (error is TargetInvocationException { InnerException: { } innerException })
            LogInternal(innerException);
    }

    private static void LogInternal(Exception error, bool withStackTrace = true)
    {
        if (!withStackTrace) {
            Log.Log(LogLevel, "{Type}, {Message}", error.GetType().Name, error.Message);
            return;
        }

        var stackTrace = error.StackTrace ?? new StackTrace().ToString();
        Log.Log(LogLevel, "{Type}, {Message}\r\n{StackTrace}", error.GetType().Name, error.Message, stackTrace);
    }
}
